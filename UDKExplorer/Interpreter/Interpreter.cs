﻿using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Be.Windows.Forms;
using System.Diagnostics;
using UDKExplorer.UDK;
using static ME3LibWV.UnrealObjectInfo;

namespace UDKExplorer
{
    public partial class Interpreter : UserControl
    {
        public event PropertyValueChangedEventHandler PropertyValueChanged;

        public UDKFile Pcc { get { return udk; } set {udk = value; defaultStructValues.Clear(); } }
        public int Index;
        public string className;
        public byte[] memory;
        public int memsize;
        public int readerpos;

        public struct PropHeader
        {
            public int name;
            public int type;
            public int size;
            public int index;
            public int offset;
        }

        public string[] Types =
        {
            "StructProperty", //0
            "IntProperty",
            "FloatProperty",
            "ObjectProperty",
            "NameProperty",
            "BoolProperty",  //5
            "ByteProperty",
            "ArrayProperty",
            "StrProperty",
            "DelegateProperty",//10
            "None",
        };

        public enum nodeType
        {
            Unknown = -1,
            StructProperty = 0,
            IntProperty = 1,
            FloatProperty = 2,
            ObjectProperty = 3,
            NameProperty = 4,
            BoolProperty = 5,
            ByteProperty = 6,
            ArrayProperty = 7,
            StrProperty = 8,
            DelegateProperty = 10,
            None,
            
            ArrayLeafObject,
            ArrayLeafName,
            ArrayLeafEnum,
            ArrayLeafStruct,
            ArrayLeafBool,
            ArrayLeafString,
            ArrayLeafFloat,
            ArrayLeafInt,
            ArrayLeafByte,

            StructLeafByte,
            StructLeafFloat,
            StructLeafDeg, //indicates this is a StructProperty leaf that is in degrees (actually unreal rotation units)
            StructLeafInt,
            StructLeafObject,
            StructLeafName,
            StructLeafBool,
            StructLeafStr,
            StructLeafArray,
            StructLeafEnum,
            StructLeafStruct,

            Root,
        }
        
        
        private int lastSetOffset = -1; //offset set by program, used for checking if user changed since set 
        private nodeType LAST_SELECTED_PROP_TYPE = nodeType.Unknown; //last property type user selected. Will use to check the current offset for type
        private TreeNode LAST_SELECTED_NODE = null; //last selected tree node
        private const int HEXBOX_MAX_WIDTH = 650;

        private UDKFile udk;
        private Dictionary<string, List<PropertyReader.Property>> defaultStructValues;

        public Interpreter()
        {
            InitializeComponent();
            SetTopLevel(false);
            defaultStructValues = new Dictionary<string, List<PropertyReader.Property>>();
        }

        public void InitInterpreter()
        {
            DynamicByteProvider db = new DynamicByteProvider(udk.Exports[Index].Data);
            hb1.ByteProvider = db;
            memory = udk.Exports[Index].Data;
            className = udk.Exports[Index].ClassName;
            StartScan();
        }

        public new void Show()
        {
            base.Show();
            //StartScan();
        }

        private void StartScan(IEnumerable<string> expandedNodes = null, string topNodeName = null, string selectedNodeName = null)
        {
            hidePropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            readerpos = PropertyReader.detectStart(udk, memory, udk.Exports[Index].ObjectFlags);
            List<PropHeader> topLevelHeaders = ReadHeadersTillNone();
            TreeNode topLevelTree = new TreeNode("0000 : " + udk.Exports[Index].ObjectName);
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                GenerateTree(topLevelTree, topLevelHeaders);
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("PARSE ERROR " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (expandedNodes != null)
            {
                int memDiff = memory.Length - memsize;
                int selectedPos = Math.Abs(Convert.ToInt32(selectedNodeName));
                int curPos = 0;
                foreach (string item in expandedNodes)
                {
                    curPos = Math.Abs(Convert.ToInt32(item));
                    if (curPos > selectedPos)
                    {
                        curPos += memDiff;
                    }
                    nodes = treeView1.Nodes.Find((item[0] == '-' ? -curPos : curPos).ToString(), true);
                    if(nodes.Length > 0)
                    {
                        foreach (var node in nodes)
                        {
                            node.Expand();
                        }
                    }
                }
            }
            nodes = treeView1.Nodes.Find(topNodeName, true);
            if (nodes.Length > 0)
            {
                treeView1.TopNode = nodes[0];
            }
            nodes = treeView1.Nodes.Find(selectedNodeName, true);
            if (nodes.Length > 0)
            {
                treeView1.SelectedNode = nodes[0];
            }
            else
            {
                treeView1.SelectedNode = treeView1.Nodes[0];
            }
            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        public void GenerateTree(TreeNode localRoot, List<PropHeader> headersList)
        {
            foreach (PropHeader header in headersList)
            {
                if (readerpos > memory.Length)
                {
                    throw new IndexOutOfRangeException(": tried to read past bounds of Export Data");
                }
                nodeType type = getType(udk.getName(header.type));
                if (type != nodeType.ArrayProperty && type != nodeType.StructProperty)
                    localRoot.Nodes.Add(GenerateNode(header));
                else
                {
                    if (type == nodeType.ArrayProperty)
                    {
                        TreeNode t = GenerateNode(header);
                        int arrayLength = BitConverter.ToInt32(memory, header.offset + 24);
                        readerpos = header.offset + 28;
                        int tmp = readerpos;
                        ArrayType arrayType;
                        try
                        {
                            arrayType = getArrayType(className, udk.getName(header.name));
                        }
                        catch (Exception)
                        {
                            arrayType = ArrayType.Int;
                        }
                        if (arrayType == ArrayType.Struct)
                        {
                            PropertyInfo info = getPropertyInfo(className, udk.getName(header.name));
                            t.Text = t.Text.Insert(t.Text.IndexOf("Size: ") - 2, $"({info.reference})");
                            for (int i = 0; i < arrayLength; i++)
                            {
                                readerpos = tmp;
                                int pos = tmp;
                                List<PropHeader> arrayListPropHeaders = ReadHeadersTillNone();
                                tmp = readerpos;
                                TreeNode n = new TreeNode(i.ToString());
                                n.Tag = nodeType.ArrayLeafStruct;
                                n.Name = (-pos).ToString();
                                t.Nodes.Add(n);
                                n = t.LastNode;
                                if (info != null && (isImmutable(info.reference) || arrayListPropHeaders.Count == 0))
                                {
                                    readerpos = pos;
                                    GenerateSpecialStruct(n, info.reference, header.size / arrayLength);
                                    tmp = readerpos;
                                }
                                else if (arrayListPropHeaders.Count > 0)
                                {
                                    GenerateTree(n, arrayListPropHeaders); 
                                }
                                else
                                {
                                    throw new Exception($"at position {readerpos.ToString("X4")}. Could not read element {i} of ArrayProperty {udk.getName(header.name)}");
                                }
                                t.LastNode.Remove();
                                t.Nodes.Add(n);
                            }
                            localRoot.Nodes.Add(t);
                        }
                        else
                        {
                            t.Text = t.Text.Insert(t.Text.IndexOf("Size: ") - 2, $"({arrayType.ToString()})");
                            int count = 0;
                            int pos;
                            for (int i = 0; i < (header.size - 4); count++)
                            {
                                pos = header.offset + 28 + i;
                                if (pos > memory.Length)
                                {
                                    throw new Exception(": tried to read past bounds of Export Data");
                                }
                                int val = BitConverter.ToInt32(memory, pos);
                                string s = pos.ToString("X4") + "|" + count + ": ";
                                TreeNode node = new TreeNode();
                                node.Name = pos.ToString();
                                if (arrayType == ArrayType.Object)
                                {
                                    node.Tag = nodeType.ArrayLeafObject;
                                    int value = val;
                                    if (value == 0)
                                    {
                                        //invalid
                                        s += "Null [" + value + "] ";
                                    }
                                    else
                                    {

                                        bool isImport = value < 0;
                                        if (isImport)
                                        {
                                            value = -value;
                                        }
                                        value--; //0-indexed
                                        if (isImport)
                                        {
                                            if (udk.Imports.Count > value)
                                            {
                                                s += udk.Imports[value].ObjectName + " [IMPORT " + value + "]";
                                            }
                                            else
                                            {
                                                s += "Index not in import list [" + value + "]";
                                            }
                                        }
                                        else
                                        {
                                            if (udk.Exports.Count > value)
                                            {
                                                s += udk.Exports[value].ObjectName + " [EXPORT " + value + "]";
                                            }
                                            else
                                            {
                                                s += "Index not in export list [" + value + "]";
                                            }
                                        }
                                    }
                                    i += 4;
                                }
                                else if (arrayType == ArrayType.Name || arrayType == ArrayType.Enum)
                                {

                                    node.Tag = arrayType == ArrayType.Name ? nodeType.ArrayLeafName : nodeType.ArrayLeafEnum;
                                    int value = val;
                                    if (value < 0)
                                    {
                                        s += "Invalid Name Index [" + value + "]";
                                    }
                                    else
                                    {
                                        if (udk.Names.Count > value)
                                        {
                                            s += $"\"{udk.Names[value]}\"_{BitConverter.ToInt32(memory, pos + 4)}[NAMEINDEX {value}]";
                                        }
                                        else
                                        {
                                            s += "Index not in name list [" + value + "]";
                                        }
                                    }
                                    i += 8;
                                }
                                else if (arrayType == ArrayType.Float)
                                {
                                    node.Tag = nodeType.ArrayLeafFloat;
                                    s += BitConverter.ToSingle(memory, pos).ToString("0.0######");
                                    i += 4;
                                }
                                else if (arrayType == ArrayType.Byte)
                                {
                                    node.Tag = nodeType.ArrayLeafByte;
                                    s += "(byte)" + memory[pos];
                                    i += 1;
                                }
                                else if (arrayType == ArrayType.Bool)
                                {
                                    node.Tag = nodeType.ArrayLeafBool;
                                    s += BitConverter.ToBoolean(memory, pos);
                                    i += 1;
                                } 
                                else if (arrayType == ArrayType.String)
                                {
                                    node.Tag = nodeType.ArrayLeafString;
                                    int sPos = pos + 4;
                                    s += "\"";
                                    int len = val > 0 ? val : -val;
                                    for (int j = 1; j < len; j++)
                                    {
                                        s += BitConverter.ToChar(memory, sPos);
                                        sPos += 2;
                                    }
                                    s += "\"";
                                    i += (len * 2) + 4;
                                }
                                else
                                {
                                    node.Tag = nodeType.ArrayLeafInt;
                                    s += val.ToString();
                                    i += 4;
                                }
                                node.Text = s;
                                t.Nodes.Add(node);
                            }
                            localRoot.Nodes.Add(t);
                        }
                    }
                    if (type == nodeType.StructProperty)
                    {
                        TreeNode t = GenerateNode(header);
                        readerpos = header.offset + 32;
                        List<PropHeader> ll = ReadHeadersTillNone();
                        if (ll.Count != 0)
                        {
                            GenerateTree(t, ll);
                        }
                        else
                        {
                            string structType = udk.getName(BitConverter.ToInt32(memory, header.offset + 24));
                            GenerateSpecialStruct(t, structType, header.size);
                        }
                        localRoot.Nodes.Add(t);
                    }

                }
            }
        }

        //structs that are serialized down to just their values.
        private void GenerateSpecialStruct(TreeNode t, string structType, int size)
        {
            TreeNode node;

            //just prints the raw hex since there's no telling what it actually is
            node = new TreeNode(readerpos.ToString("X4") + ": " + memory.Skip(readerpos).Take(size).Aggregate("", (b, s) => b + " " + s.ToString("X2")));
            node.Tag = nodeType.Unknown;
            t.Nodes.Add(node);
            readerpos += size;
            return;
            
        }

        public TreeNode GenerateNode(PropHeader p)
        {
            string s = p.offset.ToString("X4") + ": ";
            s += "Name: \"" + udk.getName(p.name) + "\" ";
            s += "Type: \"" + udk.getName(p.type) + "\" ";
            s += "Size: " + p.size + " Value: ";
            nodeType propertyType = getType(udk.getName(p.type));
            int idx;
            byte val;
            switch (propertyType)
            {
                case nodeType.IntProperty:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += idx.ToString();
                    break;
                case nodeType.ObjectProperty:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += idx +  " (" + udk.getObjectName(idx) + ")";
                    break;
                case nodeType.StrProperty:
                    int count = BitConverter.ToInt32(memory, p.offset + 24);
                    s += "\"";
                    for (int i = 0; i < count * -1 - 1; i++)
                        s += (char)memory[p.offset + 28 + i * 2];
                    s += "\"";
                    break;
                case nodeType.BoolProperty:
                    val = memory[p.offset + 24];
                    s += (val == 1).ToString();
                    break;
                case nodeType.FloatProperty:
                    float f = BitConverter.ToSingle(memory, p.offset + 24);
                    s += f.ToString("0.0######");
                    break;
                case nodeType.NameProperty:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += "\"" + udk.getName(idx) + "\"_" + BitConverter.ToInt32(memory, p.offset + 28);
                    break;
                case nodeType.StructProperty:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += "\"" + udk.getName(idx) + "\"";
                    break;
                case nodeType.ByteProperty:
                    if(p.size == 1)
                    {
                        val = memory[p.offset + 32];
                        s += val.ToString();
                    }
                    else
	                {
                        idx = BitConverter.ToInt32(memory, p.offset + 24);
                        int idx2 = BitConverter.ToInt32(memory, p.offset + 32);
                        s += "\"" + udk.getName(idx) + "\",\"" + udk.getName(idx2) + "\""; 
                    }
                    break;
                case nodeType.ArrayProperty:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += idx + "(count)";
                    break;
            }
            TreeNode ret = new TreeNode(s);
            ret.Tag = propertyType;
            ret.Name = p.offset.ToString();
            return ret;
        }

        public nodeType getType(string s)
        {
            int ret = -1;
            for (int i = 0; i < Types.Length; i++)
                if (s == Types[i])
                    ret = i;
            return (nodeType)ret;
        }

        public List<PropHeader> ReadHeadersTillNone()
        {
            List<PropHeader> ret = new List<PropHeader>();
            bool run = true;
            while (run)
            {
                PropHeader p = new PropHeader();
                if (readerpos > memory.Length || readerpos < 0)
                {
                    //nothing else to interpret.
                    run = false;
                    continue; 
                }
                p.name = BitConverter.ToInt32(memory, readerpos);
                if (!udk.isName(p.name))
                    run = false;
                else
                {
                    if (udk.getName(p.name) != "None")
                    {
                        p.type = BitConverter.ToInt32(memory, readerpos + 8);
                        if (!udk.isName(p.type) || getType(udk.getName(p.type)) == nodeType.Unknown)
                            run = false;
                        else
                        {
                            p.size = BitConverter.ToInt32(memory, readerpos + 16);
                            p.index = BitConverter.ToInt32(memory, readerpos + 20);
                            p.offset = readerpos;
                            ret.Add(p);
                            readerpos += p.size + 24;
                            if (getType(udk.getName(p.type)) == nodeType.BoolProperty)//Boolbyte
                                readerpos++;
                            if (getType(udk.getName(p.type)) == nodeType.StructProperty ||//StructName
                                getType(udk.getName(p.type)) == nodeType.ByteProperty)//byteprop
                                readerpos += 8;
                        }
                    }
                    else
                    {
                        p.type = p.name;
                        p.size = 0;
                        p.index = 0;
                        p.offset = readerpos;
                        ret.Add(p);
                        readerpos += 8;
                        run = false;
                    }
                }
            }
            return ret;
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = "*.txt|*.txt";
            d.FileName = udk.Exports[Index].ObjectName + ".txt";
            if (d.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(d.FileName, FileMode.Create, FileAccess.Write);
                PrintNodes(treeView1.Nodes, fs, 0);
                fs.Close();
                MessageBox.Show("Done.");
            }
        }

        public void PrintNodes(TreeNodeCollection t, FileStream fs, int depth)
        {
            string tab = "";
            for (int i = 0; i < depth; i++)
                tab += ' ';
            foreach (TreeNode t1 in t)
            {
                string s = tab + t1.Text;
                WriteString(fs, s);
                fs.WriteByte(0xD);
                fs.WriteByte(0xA);
                if (t1.Nodes.Count != 0)
                    PrintNodes(t1.Nodes, fs, depth + 4);
            }
        }

        public void WriteString(FileStream fs, string s)
        {
            for (int i = 0; i < s.Length; i++)
                fs.WriteByte((byte)s[i]);
        }

        private string getEnclosingType(TreeNode node)
        {
            Stack<TreeNode> nodeStack = new Stack<TreeNode>();
            string typeName = className;
            string propname;
            PropertyInfo p;
            while (node != null && !node.Tag.Equals(nodeType.Root))
            {
                nodeStack.Push(node);
                node = node.Parent;
            }
            bool isStruct = false;
            while(nodeStack.Count > 0)
            {
                node = nodeStack.Pop();
                if ((nodeType)node.Tag == nodeType.ArrayLeafStruct)
                {
                    continue;
                }
                propname = udk.getName(BitConverter.ToInt32(memory, Math.Abs(Convert.ToInt32(node.Name))));
                p = getPropertyInfo(typeName, propname, isStruct);
                typeName = p.reference;
                isStruct = true;
            }
            return typeName;
        }

        private bool isArrayLeaf(nodeType type)
        {
            return (type == nodeType.ArrayLeafBool || type == nodeType.ArrayLeafEnum || type == nodeType.ArrayLeafFloat ||
                type == nodeType.ArrayLeafInt || type == nodeType.ArrayLeafName || type == nodeType.ArrayLeafObject ||
                type == nodeType.ArrayLeafString || type == nodeType.ArrayLeafStruct || type == nodeType.ArrayLeafByte);
        }

        private bool isStructLeaf(nodeType type)
        {
            return (type == nodeType.StructLeafByte || type == nodeType.StructLeafDeg || type == nodeType.StructLeafFloat ||
                type == nodeType.StructLeafBool || type == nodeType.StructLeafInt || type == nodeType.StructLeafName ||
                type == nodeType.StructLeafStr || type == nodeType.StructLeafEnum || type == nodeType.StructLeafArray ||
                type == nodeType.StructLeafStruct || type == nodeType.StructLeafObject);
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            LAST_SELECTED_NODE = e.Node;
            hidePropEditingControls();
            if (e.Node.Name == "")
            {
                Debug.WriteLine("This node is not parsable.");
                //can't attempt to parse this.
                LAST_SELECTED_PROP_TYPE = nodeType.Unknown;
                return;
            }
            try
            {
                int off = Math.Abs(Convert.ToInt32(e.Node.Name));
                hb1.SelectionStart = off;
                lastSetOffset = off;
                hb1.SelectionLength = 1;
                if (e.Node.Tag == null)
                {
                    LAST_SELECTED_PROP_TYPE = nodeType.Unknown;
                    return;
                }
                LAST_SELECTED_PROP_TYPE = (nodeType)e.Node.Tag;
                if (isArrayLeaf(LAST_SELECTED_PROP_TYPE))
                {
                    TryParseArrayLeaf(e.Node);
                }
                else if (isStructLeaf(LAST_SELECTED_PROP_TYPE))
                {
                    TryParseStructProperty(e.Node);
                }
                else if (LAST_SELECTED_PROP_TYPE == nodeType.ArrayProperty)
                {
                    addArrayElementButton.Visible = true;
                    proptext.Clear();
                    ArrayType arrayType = getArrayType(getEnclosingType(e.Node.Parent), udk.getName(BitConverter.ToInt32(memory, off)));
                    switch (arrayType)
                    {
                        case ArrayType.Byte:
                        case ArrayType.String:
                            proptext.Visible = true;
                            break;
                        case ArrayType.Object:
                            objectNameLabel.Text = "()";
                            proptext.Visible = objectNameLabel.Visible = true;
                            break;
                        case ArrayType.Int:
                            proptext.Text = "0";
                            proptext.Visible = true;
                            break;
                        case ArrayType.Float:
                            proptext.Text = "0.0";
                            proptext.Visible = true;
                            break;
                        case ArrayType.Name:
                            proptext.Text = "0";
                            nameEntry.AutoCompleteCustomSource.AddRange(udk.Names.Select(x => x.name).ToArray());
                            proptext.Visible = nameEntry.Visible = true;
                            break;
                        case ArrayType.Bool:
                            propDropdown.Items.Clear();
                            propDropdown.Items.Add("False");
                            propDropdown.Items.Add("True");
                            propDropdown.Visible = true;
                            break;
                        case ArrayType.Enum:
                            string enumName = getEnclosingType(e.Node);
                            List<string> values = getEnumValues(enumName, true);
                            if (values == null)
                            {
                                addArrayElementButton.Visible = false;
                                return;
                            }
                            propDropdown.Items.Clear();
                            propDropdown.Items.AddRange(values.ToArray());
                            propDropdown.Visible = true;
                            break;
                        case ArrayType.Struct:
                        default:
                            break;
                    }
                }
                else if (LAST_SELECTED_PROP_TYPE == nodeType.Root)
                {
                }
                else if (LAST_SELECTED_PROP_TYPE == nodeType.None && e.Node.Parent.Tag != null && e.Node.Parent.Tag.Equals(nodeType.Root))
                {
                }
                else
                {
                    TryParseProperty();
                }
            }
            catch (Exception)
            {
                Debug.WriteLine("Node name is not in correct format.");
                //name is wrong, don't attempt to continue parsing.
                LAST_SELECTED_PROP_TYPE = nodeType.Unknown;
                return;
            }
        }

        private void hidePropEditingControls()
        {
            objectNameLabel.Visible = nameEntry.Visible = proptext.Visible = setPropertyButton.Visible = propDropdown.Visible = 
                addArrayElementButton.Visible = deleteArrayElementButton.Visible = moveDownButton.Visible =
                moveUpButton.Visible = false;
        }

        private void TryParseProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int type = BitConverter.ToInt32(memory, pos + 8);
                int test = BitConverter.ToInt32(memory, pos + 12);
                if (test != 0 || !udk.isName(type))
                    return;
                switch (udk.getName(type))
                {
                    case "IntProperty":
                    case "StringRefProperty":
                        proptext.Text = BitConverter.ToInt32(memory, pos + 24).ToString();
                        proptext.Visible = true;
                        break;
                    case "ObjectProperty":
                        int n = BitConverter.ToInt32(memory, pos + 24);
                        objectNameLabel.Text = $"({udk.getObjectName(n)})";
                        proptext.Text = n.ToString();
                        objectNameLabel.Visible = proptext.Visible = true;
                        break;
                    case "FloatProperty":
                        proptext.Text = BitConverter.ToSingle(memory, pos + 24).ToString();
                        proptext.Visible = true;
                        break;
                    case "BoolProperty":
                        propDropdown.Items.Clear();
                        propDropdown.Items.Add("False");
                        propDropdown.Items.Add("True");
                        propDropdown.SelectedIndex = memory[pos + 24];
                        propDropdown.Visible = true;
                        break;
                    case "NameProperty":
                        proptext.Text  = BitConverter.ToInt32(memory, pos + 28).ToString();
                        nameEntry.Text = udk.getName(BitConverter.ToInt32(memory, pos + 24));
                        nameEntry.AutoCompleteCustomSource.AddRange(udk.Names.Select(x => x.name).ToArray());
                        nameEntry.Visible = true;
                        proptext.Visible = true;
                        break;
                    case "StrProperty":
                        string s = "";
                        int count = -(int)BitConverter.ToInt64(memory, pos + 24);
                        pos += 28;
                        for (int i = 0; i < count; i++)
                        {
                            s += (char)memory[pos + i*2];
                        }
                        proptext.Text = s;
                        proptext.Visible = true;
                        break;
                    case "ByteProperty":
                        string enumName = udk.getName(BitConverter.ToInt32(memory, pos + 24));
                        if (enumName != "None")
                        {
                            try
                            {
                                List<string> values = getEnumValues(enumName, true);
                                if (values != null)
                                {
                                    propDropdown.Items.Clear();
                                    propDropdown.Items.AddRange(values.ToArray());
                                    string curVal = udk.getName(BitConverter.ToInt32(memory, pos + 32));
                                    int idx = values.IndexOf(curVal);
                                    propDropdown.SelectedIndex = idx;
                                    propDropdown.Visible = true;
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                        else
                        {
                            proptext.Text = memory[pos + 32].ToString();
                            proptext.Visible = true;
                        }
                        break;
                    default:
                        return;
                }
                setPropertyButton.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void TryParseStructProperty(TreeNode node)
        {
            try
            {
                nodeType type = (nodeType)node.Tag;
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 8)
                    return;
                switch (type)
                {
                    case nodeType.StructLeafFloat:
                        proptext.Text = BitConverter.ToSingle(memory, pos).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.StructLeafByte:
                        proptext.Text = memory[pos].ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.StructLeafBool:
                        propDropdown.Items.Clear();
                        propDropdown.Items.Add("False");
                        propDropdown.Items.Add("True");
                        propDropdown.SelectedIndex = memory[pos];
                        propDropdown.Visible = true;
                        break;
                    case nodeType.StructLeafDeg:
                        proptext.Text = (BitConverter.ToInt32(memory, pos) * 360f / 65536f).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.StructLeafObject:
                        int n = BitConverter.ToInt32(memory, pos);
                        objectNameLabel.Text = $"({udk.getObjectName(n)})";
                        proptext.Text = n.ToString();
                        proptext.Visible = objectNameLabel.Visible = true;
                        break;
                    case nodeType.StructLeafInt:
                        proptext.Text = BitConverter.ToInt32(memory, pos).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.StructLeafName:
                        proptext.Text = BitConverter.ToInt32(memory, pos + 4).ToString();
                        nameEntry.Text = udk.getName(BitConverter.ToInt32(memory, pos));
                        nameEntry.AutoCompleteCustomSource.AddRange(udk.Names.Select(x => x.name).ToArray());
                        nameEntry.Visible = proptext.Visible = true;
                        break;
                    case nodeType.StructLeafStr:
                        string s = "";
                        int count = -BitConverter.ToInt32(memory, pos);
                        for (int i = 0; i < count - 1; i++)
                        {
                            s += (char)memory[pos + 4 + i * 2];
                        }
                        proptext.Text = s;
                        proptext.Visible = true;
                        break;
                    case nodeType.StructLeafEnum:
                        int begin = node.Text.LastIndexOf(':') + 3;
                        string enumName = node.Text.Substring(begin, node.Text.IndexOf(',') - 1 - begin);
                        List<string> values = getEnumValues(enumName, true);
                        if (values == null)
                        {
                            return;
                        }
                        propDropdown.Items.Clear();
                        propDropdown.Items.AddRange(values.ToArray());
                        setPropertyButton.Visible = propDropdown.Visible = true;
                        string curVal = udk.getName(BitConverter.ToInt32(memory, pos));
                        int idx = values.IndexOf(curVal);
                        propDropdown.SelectedIndex = idx;
                        break;
                    default:
                        return;
                }
                setPropertyButton.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void TryParseArrayLeaf(TreeNode node)
        {
            try
            {
                nodeType type = (nodeType)node.Tag;
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 8)
                    return;
                switch (type)
                {
                    case nodeType.ArrayLeafInt:
                        proptext.Text = BitConverter.ToInt32(memory, pos).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafObject:
                        int n = BitConverter.ToInt32(memory, pos);
                        objectNameLabel.Text = $"({udk.getObjectName(n)})";
                        proptext.Text = n.ToString();
                        proptext.Visible = objectNameLabel.Visible = true;
                        break;
                    case nodeType.ArrayLeafFloat:
                        proptext.Text = BitConverter.ToSingle(memory, pos).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafBool:
                        propDropdown.Items.Clear();
                        propDropdown.Items.Add("False");
                        propDropdown.Items.Add("True");
                        propDropdown.SelectedIndex = memory[pos];
                        propDropdown.Visible = true;
                        break;
                    case nodeType.ArrayLeafByte:
                        proptext.Text = memory[pos].ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafName:
                        proptext.Text = BitConverter.ToInt32(memory, pos + 4).ToString();
                        nameEntry.Text = udk.getName(BitConverter.ToInt32(memory, pos));
                        nameEntry.AutoCompleteCustomSource.AddRange(udk.Names.Select(x => x.name).ToArray());
                        proptext.Visible = nameEntry.Visible = true;
                        break;
                    case nodeType.ArrayLeafString:
                        string s = "";
                        int count = -BitConverter.ToInt32(memory, pos);
                        for (int i = 0; i < count - 1; i++)
                        {
                            s += (char)memory[pos + 4 + i * 2];
                        }
                        proptext.Text = s;
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafEnum:
                        string enumName = getEnclosingType(node.Parent);
                        List<string> values = getEnumValues(enumName, true);
                        if (values == null)
                        {
                            return;
                        }
                        propDropdown.Items.Clear();
                        propDropdown.Items.AddRange(values.ToArray());
                        propDropdown.Visible = true;
                        string curVal = udk.getName(BitConverter.ToInt32(memory, pos));
                        int idx = values.IndexOf(curVal);
                        propDropdown.SelectedIndex = idx;
                        break;
                    case nodeType.ArrayLeafStruct:
                        break;
                    default:
                        return;
                }
                deleteArrayElementButton.Visible = setPropertyButton.Visible = addArrayElementButton.Visible = true;
                if (type == nodeType.ArrayLeafStruct)
                {
                    setPropertyButton.Visible = false;
                }
                if (node.NextNode != null)
                {
                    moveDownButton.Visible = true;
                }
                if (node.PrevNode != null)
                {
                    moveUpButton.Visible = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setProperty_Click(object sender, EventArgs e)
        {
            if (hb1.SelectionStart != lastSetOffset)
            {
                return; //user manually moved cursor
            }
            if (isArrayLeaf(LAST_SELECTED_PROP_TYPE))
            {
                setArrayProperty();
            }
            else if(isStructLeaf(LAST_SELECTED_PROP_TYPE))
            {
                setStructProperty();
            }
            else
            {
                setNonArrayProperty();
            }
        }

        private void setStructProperty()
        {
            try
            {
                int pos = lastSetOffset;
                if (memory.Length - pos < 8)
                    return;
                byte b = 0;
                float f = 0;
                int i = 0;
                switch (LAST_SELECTED_PROP_TYPE)
                {
                    case nodeType.StructLeafByte:
                        if (byte.TryParse(proptext.Text, out b))
                        {
                            memory[pos] = b;
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.StructLeafBool:
                        memory[pos] = (byte)propDropdown.SelectedIndex;
                        RefreshMem(pos);
                        break;
                    case nodeType.StructLeafFloat:
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(f));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.StructLeafDeg:
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(Convert.ToInt32(f * 65536f / 360f)));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.StructLeafObject:
                    case nodeType.StructLeafInt:
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (int.TryParse(proptext.Text, out i))
                        {
                            WriteMem(pos, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.StructLeafEnum:
                        i = udk.FindNameOrAdd(propDropdown.SelectedItem as string);
                        WriteMem(pos, BitConverter.GetBytes(i));
                        RefreshMem(pos);
                        break;
                    case nodeType.StructLeafName:
                        if (int.TryParse(proptext.Text, out i))
                        {
                            if (!udk.Names.Any(x => x.name == nameEntry.Text) &&
                                DialogResult.No == MessageBox.Show($"{Path.GetFileName(udk.FileName)} does not contain the Name: {nameEntry.Text}\nWould you like to add it to the Name list?", "", MessageBoxButtons.YesNo))
                            {
                                break;
                            }
                            WriteMem(pos, BitConverter.GetBytes(udk.FindNameOrAdd(nameEntry.Text)));
                            WriteMem(pos + 4, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.StructLeafStr:
                        string s = proptext.Text;
                        int offset = pos;
                        int oldLength = -(int)BitConverter.ToInt64(memory, offset);
                        int oldSize = 4 + (oldLength * 2);
                        List<byte> stringBuff = new List<byte>(s.Length * 2);
                        for (int j = 0; j < s.Length; j++)
                        {
                            stringBuff.AddRange(BitConverter.GetBytes(s[j]));
                        }
                        stringBuff.Add(0);
                        stringBuff.Add(0);
                        byte[] buff = BitConverter.GetBytes(-(s.LongCount() + 1));
                        for (int j = 0; j < 8; j++)
                            memory[offset + j] = buff[j];
                        buff = new byte[memory.Length - (oldLength * 2) + stringBuff.Count];
                        int startLength = offset + 4;
                        int startLength2 = startLength + (oldLength * 2);
                        for (int j = 0; j < startLength; j++)
                        {
                            buff[j] = memory[j];
                        }
                        for (int j = 0; j < stringBuff.Count; j++)
                        {
                            buff[j + startLength] = stringBuff[j];
                        }
                        startLength += stringBuff.Count;
                        for (int j = 0; j < memory.Length - startLength2; j++)
                        {
                            buff[j + startLength] = memory[j + startLength2];
                        }
                        memory = buff;

                        //bubble up size
                        TreeNode parent = LAST_SELECTED_NODE.Parent;
                        while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) ||
                            parent.Tag.Equals(nodeType.ArrayLeafStruct) || isStructLeaf((nodeType)parent.Tag)))
                        {
                            if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct || isStructLeaf((nodeType)parent.Tag))
                            {
                                parent = parent.Parent;
                                continue;
                            }
                            updateArrayLength(Math.Abs(Convert.ToInt32(parent.Name)), 0, (stringBuff.Count + 4) - oldSize);
                            parent = parent.Parent;
                        }
                        RefreshMem(pos);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setNonArrayProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int type = BitConverter.ToInt32(memory, pos + 8);
                int test = BitConverter.ToInt32(memory, pos + 12);
                if (test != 0 || !udk.isName(type))
                    return;
                int i = 0;
                float f = 0;
                byte b = 0;
                switch (udk.getName(type))
                {
                    case "IntProperty":
                    case "ObjectProperty":
                    case "StringRefProperty":
                        if (int.TryParse(proptext.Text, out i))
                        {
                            WriteMem(pos + 24, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        break;
                    case "NameProperty":
                        if (int.TryParse(proptext.Text, out i))
                        {
                            if (!udk.Names.Any(x => x.name == nameEntry.Text) &&
                                DialogResult.No == MessageBox.Show($"{Path.GetFileName(udk.FileName)} does not contain the Name: {nameEntry.Text}\nWould you like to add it to the Name list?", "", MessageBoxButtons.YesNo))
                            {
                                break;
                            }
                            WriteMem(pos + 24, BitConverter.GetBytes(udk.FindNameOrAdd(nameEntry.Text)));
                            WriteMem(pos + 28, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        break;
                    case "FloatProperty":
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos + 24, BitConverter.GetBytes(f));
                            RefreshMem(pos);
                        }
                        break;
                    case "BoolProperty":
                        memory[pos + 24] = (byte)propDropdown.SelectedIndex;
                        RefreshMem(pos);
                        break;
                    case "ByteProperty":
                        if (propDropdown.Visible)
                        {
                            i = udk.FindNameOrAdd(propDropdown.SelectedItem as string);
                            WriteMem(pos + 32, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        else if(byte.TryParse(proptext.Text, out b))
                        {
                            memory[pos + 32] = b;
                            RefreshMem(pos);
                        }
                        break;
                    case "StrProperty":
                        string s = proptext.Text;
                        int offset = pos + 24;
                        int oldSize = BitConverter.ToInt32(memory, pos + 16);
                        int oldLength = -(int)BitConverter.ToInt64(memory, offset);
                        List<byte> stringBuff = new List<byte>(s.Length * 2);
                        for (int j = 0; j < s.Length; j++)
                        {
                            stringBuff.AddRange(BitConverter.GetBytes(s[j]));
                        }
                        stringBuff.Add(0);
                        stringBuff.Add(0);
                        byte[] buff = BitConverter.GetBytes((s.LongCount() + 1) * 2 + 4);
                        for (int j = 0; j < 4; j++)
                            memory[offset - 8 + j] = buff[j];
                        buff = BitConverter.GetBytes(-(s.Count() + 1));
                        for (int j = 0; j < 4; j++)
                            memory[offset + j] = buff[j];
                        buff = new byte[memory.Length - (oldLength * 2) + stringBuff.Count];
                        int startLength = offset + 4;
                        int startLength2 = startLength + (oldLength * 2);
                        for (int j = 0; j < startLength; j++)
                        {
                            buff[j] = memory[j];
                        }
                        for (int j = 0; j < stringBuff.Count; j++)
                        {
                            buff[j + startLength] = stringBuff[j];
                        }
                        startLength += stringBuff.Count;
                        for (int j = 0; j < memory.Length - startLength2; j++)
                        {
                            buff[j + startLength] = memory[j + startLength2];
                        }
                        memory = buff;

                        //bubble up size
                        TreeNode parent = LAST_SELECTED_NODE.Parent;
                        while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
                        {
                            if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                            {
                                parent = parent.Parent;
                                continue;
                            }
                            updateArrayLength(Math.Abs(Convert.ToInt32(parent.Name)), 0, (stringBuff.Count + 4) - oldSize);
                            parent = parent.Parent;
                        }
                        RefreshMem(pos);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setArrayProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 8)
                    return;
                int i = 0;
                byte b = 0;
                switch (LAST_SELECTED_PROP_TYPE)
                {
                    case nodeType.ArrayLeafInt:
                    case nodeType.ArrayLeafObject:
                        if (int.TryParse(proptext.Text, out i))
                        {
                            WriteMem(pos, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafFloat:
                        proptext.Text = CheckSeperator(proptext.Text);
                        float f = 0f;
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(f));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafByte:
                        if (byte.TryParse(proptext.Text, out b))
                        {
                            memory[pos] = b;
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafBool:
                        memory[pos] = (byte)propDropdown.SelectedIndex;
                        RefreshMem(pos);
                        break;
                    case nodeType.ArrayLeafName:
                        if (int.TryParse(proptext.Text, out i))
                        {
                            if (!udk.Names.Any(x => x.name == nameEntry.Text) &&
                                DialogResult.No == MessageBox.Show($"{Path.GetFileName(udk.FileName)} does not contain the Name: {nameEntry.Text}\nWould you like to add it to the Name list?", "", MessageBoxButtons.YesNo))
                            {
                                break;
                            }
                            WriteMem(pos, BitConverter.GetBytes(udk.FindNameOrAdd(nameEntry.Text)));
                            WriteMem(pos + 4, BitConverter.GetBytes(i));
                            RefreshMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafEnum:
                        i = udk.FindNameOrAdd(propDropdown.SelectedItem as string);
                        WriteMem(pos, BitConverter.GetBytes(i));
                        RefreshMem(pos);
                        break;
                    case nodeType.ArrayLeafString:
                        string s = proptext.Text;
                        int offset = pos;
                        int oldLength = -(int)BitConverter.ToInt64(memory, offset);
                        int oldSize = 4 + (oldLength * 2);
                        List<byte> stringBuff = new List<byte>(s.Length * 2);
                        for (int j = 0; j < s.Length; j++)
                        {
                            stringBuff.AddRange(BitConverter.GetBytes(s[j]));
                        }
                        stringBuff.Add(0);
                        stringBuff.Add(0);
                        byte[] buff = BitConverter.GetBytes(-(s.LongCount() + 1));
                        for (int j = 0; j < 8; j++)
                            memory[offset + j] = buff[j];
                        buff = new byte[memory.Length - (oldLength * 2) + stringBuff.Count];
                        int startLength = offset + 4;
                        int startLength2 = startLength + (oldLength * 2);
                        for (int j = 0; j < startLength; j++)
                        {
                            buff[j] = memory[j];
                        }
                        for (int j = 0; j < stringBuff.Count; j++)
                        {
                            buff[j + startLength] = stringBuff[j];
                        }
                        startLength += stringBuff.Count;
                        for (int j = 0; j < memory.Length - startLength2; j++)
                        {
                            buff[j + startLength] = memory[j + startLength2];
                        }
                        memory = buff;

                        //bubble up size
                        TreeNode parent = LAST_SELECTED_NODE.Parent;
                        while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
                        {
                            if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                            {
                                parent = parent.Parent;
                                continue;
                            }
                            updateArrayLength(Math.Abs(Convert.ToInt32(parent.Name)), 0, (stringBuff.Count + 4) - oldSize);
                            parent = parent.Parent;
                        }
                        RefreshMem(pos);
                        break;
                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private byte[] deleteArrayLeaf()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (hb1.SelectionStart != lastSetOffset)
                {
                    return new byte[0]; //user manually moved cursor
                }

                if (memory.Length - pos < 8) //not long enough to deal with
                    return new byte[0];

                byte[] removedBytes;
                TreeNode parent = LAST_SELECTED_NODE.Parent;
                int leafOffset = Math.Abs(Convert.ToInt32(LAST_SELECTED_NODE.Name));
                int parentOffset = Math.Abs(Convert.ToInt32(parent.Name));
                
                int size;
                switch (LAST_SELECTED_PROP_TYPE)
                {
                    case nodeType.ArrayLeafInt:
                    case nodeType.ArrayLeafFloat:
                    case nodeType.ArrayLeafObject:
                        size = 4;
                        break;
                    case nodeType.ArrayLeafName:
                    case nodeType.ArrayLeafEnum:
                        size = 8;
                        break;
                    case nodeType.ArrayLeafBool:
                    case nodeType.ArrayLeafByte:
                        size = 1;
                        break;
                    case nodeType.ArrayLeafString:
                        size = BitConverter.ToInt32(memory, leafOffset) * -2 + 4;
                        break;
                    case nodeType.ArrayLeafStruct:
                        int tmp = readerpos = leafOffset;
                        ReadHeadersTillNone();
                        size = readerpos - tmp;
                        break;
                    default:
                        return new byte[0];
                }
                removedBytes = memory.Skip(leafOffset).Take(size).ToArray();
                //bubble up size
                bool firstbubble = true;
                while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
                {
                    if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                    {
                        parent = parent.Parent;
                        continue;
                    }
                    parentOffset = Math.Abs(Convert.ToInt32(parent.Name));
                    if (firstbubble)
                    {
                        memory = RemoveIndices(memory, leafOffset, size);
                        firstbubble = false;
                        updateArrayLength(parentOffset, -1, -size);
                    }
                    else
                    {
                        updateArrayLength(parentOffset, 0, -size);
                    }
                    parent = parent.Parent;
                }
                if (LAST_SELECTED_PROP_TYPE == nodeType.ArrayLeafStruct)
                {
                    RefreshMem(-pos);
                }
                else
                {
                    RefreshMem(pos);
                }
                return removedBytes;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return new byte[0];
            }
        }

        private void addArrayLeaf()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (hb1.SelectionStart != lastSetOffset)
                {
                    return; //user manually moved cursor
                }
                bool isLeaf = false;
                int leafOffset = 0;
                //is a leaf
                if (deleteArrayElementButton.Visible == true)
                {
                    isLeaf = true;
                    leafOffset = pos;
                    pos = Math.Abs(Convert.ToInt32(LAST_SELECTED_NODE.Parent.Name));
                    LAST_SELECTED_NODE = LAST_SELECTED_NODE.Parent;
                }
                int size = BitConverter.ToInt32(memory, pos + 16);
                int count = BitConverter.ToInt32(memory, pos + 24);
                int leafSize = 0;
                string propName = udk.getName(BitConverter.ToInt32(memory, pos));
                ArrayType arrayType = getArrayType(getEnclosingType(LAST_SELECTED_NODE.Parent), propName);
                List<byte> memList = memory.ToList();
                int i;
                float f;
                byte b = 0;
                int offset;
                if (isLeaf)
                {
                    offset = leafOffset;
                }
                else
                {
                    offset = pos + 24 + size;
                }
                switch (arrayType)
                {
                    case ArrayType.Int:
                    case ArrayType.Object:
                        leafSize = 4;
                        if (!int.TryParse(proptext.Text, out i))
                        {
                            return; //not valid element
                        }
                        memList.InsertRange(offset, BitConverter.GetBytes(i));
                        break;
                    case ArrayType.Float:
                        leafSize = 4;
                        if (!float.TryParse(proptext.Text, out f))
                        {
                            return; //not valid element
                        }
                        memList.InsertRange(offset, BitConverter.GetBytes(f));
                        break;
                    case ArrayType.Byte:
                        leafSize = 1;
                        if (!byte.TryParse(proptext.Text, out b))
                        {
                            return; //not valid
                        }
                        memList.Insert(offset, b);
                        break;
                    case ArrayType.Bool:
                        leafSize = 1;
                        memList.Insert(offset, (byte)propDropdown.SelectedIndex);
                        break;
                    case ArrayType.Name:
                        leafSize = 8;
                        if (!int.TryParse(proptext.Text, out i))
                        {
                            return; //not valid
                        }
                        if (!udk.Names.Any(x => x.name == nameEntry.Text) &&
                            DialogResult.No == MessageBox.Show($"{Path.GetFileName(udk.FileName)} does not contain the Name: {nameEntry.Text}\nWould you like to add it to the Name list?", "", MessageBoxButtons.YesNo))
                        {
                            return;
                        }
                        memList.InsertRange(offset, BitConverter.GetBytes(udk.FindNameOrAdd(nameEntry.Text)));
                        memList.InsertRange(offset + 4, BitConverter.GetBytes(i));
                        break;
                    case ArrayType.Enum:
                        leafSize = 8;
                        string selectedItem = propDropdown.SelectedItem as string;
                        if (selectedItem == null)
                        {
                            return;
                        }
                        i = udk.FindNameOrAdd(selectedItem);
                        memList.InsertRange(offset, BitConverter.GetBytes(i));
                        memList.InsertRange(offset + 4, new byte[4]);
                        break;
                    case ArrayType.String:
                        memList.InsertRange(offset, BitConverter.GetBytes(-(proptext.Text.Length + 1)));
                        List<byte> stringBuff = new List<byte>();
                        for (int j = 0; j < proptext.Text.Length; j++)
                        {
                            stringBuff.AddRange(BitConverter.GetBytes(proptext.Text[j]));
                        }
                        stringBuff.Add(0);
                        stringBuff.Add(0);
                        memList.InsertRange(offset + 4, stringBuff);
                        leafSize = 4 + stringBuff.Count;
                        break;
                    case ArrayType.Struct:
                        throw new Exception("Cannot add new struct property");
                        break;
                    default:
                        return;
                }
                memory = memList.ToArray();
                updateArrayLength(pos, 1, leafSize);

                //bubble up size
                TreeNode parent = LAST_SELECTED_NODE.Parent;
                while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
                {
                    if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                    {
                        parent = parent.Parent;
                        continue;
                    }
                    updateArrayLength(Math.Abs(Convert.ToInt32(parent.Name)), 0, leafSize);
                    parent = parent.Parent;
                }
                RefreshMem(arrayType == ArrayType.Struct ? -offset : offset);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private T[] RemoveIndices<T>(T[] IndicesArray, int RemoveAt, int NumElementsToRemove)
        {
            if (RemoveAt < 0 || RemoveAt > IndicesArray.Length - 1 || NumElementsToRemove < 0 || NumElementsToRemove + RemoveAt > IndicesArray.Length - 1)
            {
                return IndicesArray;
            }
            T[] newIndicesArray = new T[IndicesArray.Length - NumElementsToRemove];

            int i = 0;
            int j = 0;
            while (i < IndicesArray.Length)
            {
                if (i < RemoveAt || i >= RemoveAt + NumElementsToRemove)
                {                    
                    newIndicesArray[j] = IndicesArray[i];
                    j++;
                } else
                {
                    //Debug.WriteLine("Skipping byte: " + i.ToString("X4"));
                }

                i++;
            }

            return newIndicesArray;
        }

        private void WriteMem(int pos, byte[] buff)
        {
            for (int i = 0; i < buff.Length; i++)
                memory[pos + i] = buff[i];
        }

        /// <summary>
        /// Updates an array properties length and size in bytes. Does not refresh the memory view
        /// </summary>
        /// <param name="startpos">Starting index of the array property</param>
        /// <param name="countDelta">Delta in terms of how many items the array has</param>
        /// <param name="byteDelta">Delta in terms of how many bytes the array data is</param>
        private void updateArrayLength(int startpos, int countDelta, int byteDelta)
        {
            int sizeOffset = 16;
            int countOffset = 24;
            int oldSize = BitConverter.ToInt32(memory, sizeOffset + startpos);
            int oldCount = BitConverter.ToInt32(memory, countOffset + startpos);

            int newSize = oldSize + byteDelta;
            int newCount = oldCount + countDelta;

            WriteMem(startpos + sizeOffset, BitConverter.GetBytes(newSize));
            WriteMem(startpos + countOffset, BitConverter.GetBytes(newCount));

        }


        private void RefreshMem(int? selectedNodePos = null)
        {
            udk.Exports[Index].Data = memory;
            hb1.ByteProvider = new DynamicByteProvider(memory);
            //adds rootnode to list
            List<TreeNode> allNodes = treeView1.Nodes.Cast<TreeNode>().ToList();
            //flatten tree of nodes into list.
            for (int i = 0; i < allNodes.Count(); i++)
            {
                allNodes.AddRange(allNodes[i].Nodes.Cast<TreeNode>());
            }

            var expandedNodes = allNodes.Where(x => x.IsExpanded).Select(x => x.Name);
            StartScan(expandedNodes, treeView1.TopNode.Name, selectedNodePos?.ToString());
            PropertyValueChanged?.Invoke(this, new PropertyValueChangedEventArgs(null, null));
        }

        private string CheckSeperator(string s)
        {
            string seperator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            string wrongsep;
            if (seperator == ".")
                wrongsep = ",";
            else
                wrongsep = ".";
            return s.Replace(wrongsep, seperator);
        }

        private void expandAllButton_Click(object sender, EventArgs e)
        {
            if (treeView1 != null)
            {
                treeView1.ExpandAll();
            }
        }

        private void collapseAllButton_Click(object sender, EventArgs e)
        {
            if (treeView1 != null)

            {
                treeView1.CollapseAll();
                treeView1.Nodes[0].Expand();
            }
        }

        private void deleteArrayElement_Click(object sender, EventArgs e)
        {
            deleteArrayLeaf();
        }

        private void addArrayElementButton_Click(object sender, EventArgs e)
        {
             addArrayLeaf();
        }

        private void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag != null && e.Node.Tag.Equals(nodeType.ArrayProperty) && e.Node.Nodes.Count == 1)
            {
                e.Node.Nodes[0].Expand();
            }
        }

        private void proptext_KeyUp(object sender, KeyEventArgs e)
        {
            if (objectNameLabel.Visible)
            {
                int i;
                if (int.TryParse(proptext.Text, out i))
                {
                    objectNameLabel.Text = $"({udk.getObjectName(i)})";
                }
                else
                {
                    objectNameLabel.Text = "()";
                }
            }
        }

        private void moveUpButton_Click(object sender, EventArgs e)
        {
            moveElement(true);
        }

        private void moveDownButton_Click(object sender, EventArgs e)
        {
            moveElement(false);
        }

        private void moveElement(bool up)
        {
            if (hb1.SelectionStart != lastSetOffset)
            {
                return;//user manually moved cursor
            }
            int pos;
            TreeNode node;
            TreeNode parent = LAST_SELECTED_NODE.Parent;
            if (up)
            {
                node = LAST_SELECTED_NODE.PrevNode;
                pos = Math.Abs(Convert.ToInt32(node.Name));
            }
            else
            {
                node = LAST_SELECTED_NODE.NextNode;
                pos = Math.Abs(Convert.ToInt32(node.Name));
                //account for structs not neccesarily being the same size
                if (node.Nodes.Count > 0)
                {
                    //position of element being moved down + size of struct below it
                    pos = lastSetOffset + (Math.Abs(Convert.ToInt32(node.LastNode.Name)) + 8 - pos);
                }
            }
            byte[] element = deleteArrayLeaf();
            List<byte> memList = memory.ToList();
            memList.InsertRange(pos, element);
            memory = memList.ToArray();
            //bubble up size
            bool firstbubble = true;
            int parentOffset;
            while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
            {
                if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                {
                    parent = parent.Parent;
                    continue;
                }
                parentOffset = Math.Abs(Convert.ToInt32(parent.Name));
                if (firstbubble)
                {
                    firstbubble = false;
                    updateArrayLength(parentOffset, 1, element.Length);
                }
                else
                {
                    updateArrayLength(parentOffset, 0, element.Length);
                }
                parent = parent.Parent;
            }
            if (node.Nodes.Count > 0)
            {
                RefreshMem(-pos);
            }
            else
            {
                RefreshMem(pos);
            }
        }

        private void splitContainer1_SplitterMoving(object sender, SplitterCancelEventArgs e)
        {
            //hack to set max width for SplitContainer1
            splitContainer1.Panel2MinSize = splitContainer1.Width - HEXBOX_MAX_WIDTH;
        }

        private void toggleHexWidthButton_Click(object sender, EventArgs e)
        {
            if (splitContainer1.SplitterDistance > splitContainer1.Panel1MinSize)
            {
                splitContainer1.SplitterDistance = splitContainer1.Panel1MinSize;
            }
            else
            {
                splitContainer1.SplitterDistance = HEXBOX_MAX_WIDTH;
            }
        }

        private void hb1_SelectionChanged(object sender, EventArgs e)
        {
            int start = (int)hb1.SelectionStart;
            int len = (int)hb1.SelectionLength;
            int size = (int)hb1.ByteProvider.Length;
            if (memory != null && start != -1 && start + len <= size)
            {
                string s = $"Byte: {memory[start]}";
                if (start <= memory.Length - 4)
                {
                    s += $", Int: {BitConverter.ToInt32(memory, start)}";
                }
                s += $" | Start=0x{start.ToString("X8")} ";
                if (len > 0)
                {
                    s += $"Length=0x{len.ToString("X8")} ";
                    s += $"End=0x{(start + len - 1).ToString("X8")}"; 
                }
                selectionStatus.Text = s;
            }
            else
            {
                selectionStatus.Text = "Nothing Selected";
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = e.Node;
                if (e.Node.Nodes.Count != 0)
                {
                    nodeContextMenuStrip1.Show(MousePosition); 
                }
            }
        }

        private void expandAllChildrenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1.SelectedNode.ExpandAll();
        }

        private void collapseAllChildrenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1.SelectedNode.Collapse(false);
        }
    }
}
