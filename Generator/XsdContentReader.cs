﻿using Log;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace ClassGenerator.Generator
{
    class XsdContentReader
    {
        readonly string S;
        readonly string S1;
        readonly string S2;
        readonly string S3;

        public XsdContentReader()
        {
            S1 = new String(' ', 4);
            S = new String(' ', 8);
            S2 = new String(' ', 12);
            S3 = new String(' ', 16);
            log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        ILog log;
        bool wasParent, wasVar, wasList, wasItem, wasLong, wasLongB;
        StringBuilder pr, pu, fb, fx, tx, si, si1, si2, si3;
        Dictionary<string, XmlSchemaSimpleType> simpleTypes;
        Dictionary<string, XmlSchemaComplexType> complexTypes;
        Dictionary<string, List<XmlSchemaComplexType>> listComplexTypes;
        Dictionary<XmlSchemaType, XmlSchemaElement> complexTypeElem = new Dictionary<XmlSchemaType, XmlSchemaElement>();
        Dictionary<string, bool> complexTypeElemNames = new Dictionary<string, bool>();
        Dictionary<string, string> sameTypes;
        Dictionary<string, bool> tblList;
        Dictionary<string, bool> fldList;
        XsdContentReaderOptions _opt;

        public List<string> GenerateClasses(XsdContentReaderOptions opt, bool deleteFiles = true)
        {
            if (opt.FileByFile)
            {
                var resContent = new List<string>();
                for (int i1 = 0; i1 < opt.Files.Count; i1++)
                {
                    var f = opt.Files[i1];
                    var opt1 = new XsdContentReaderOptions { CSharpNamespace = opt.CSharpNamespace, StoreDB = opt.StoreDB, StoreDBPrefix = opt.StoreDBPrefix, ExactDBNames = opt.ExactDBNames };
                    opt1.Files.Add(f);
                    resContent.AddRange(GenerateClasses(opt1, i1 == 0 ? true : false));
                }
                return resContent;
            }

            XmlSchemaSet ss = new XmlSchemaSet();
            Dictionary<string, string> nsMap = null;

            var fileSet = new Dictionary<string, string>();
            foreach (var f in opt.Files)
            {
                log.Debug("Reading file: " + f.FileName);
                var s = ss.Add(null, f.FileName);
                if (!fileSet.ContainsKey(s.TargetNamespace))
                    fileSet.Add(s.TargetNamespace, Path.GetFileNameWithoutExtension(f.FileName) + ".cs");
                if (f.ShortNamespace != null && s.TargetNamespace != null)
                {
                    if (nsMap == null)
                        nsMap = new Dictionary<string, string>();
                    if (!nsMap.ContainsKey(s.TargetNamespace))
                        nsMap.Add(s.TargetNamespace, f.ShortNamespace);
                }
            }
            log.Debug("Compile schema set");
            ss.Compile();

            const string gDir = "code";
            if (deleteFiles)
            {
                log.Debug("Empty directory for generated classes");
                if (Directory.Exists(gDir))
                    Directory.Delete(gDir, true);
                Directory.CreateDirectory(gDir);
            }

            simpleTypes = new Dictionary<string, XmlSchemaSimpleType>();
            complexTypes = new Dictionary<string, XmlSchemaComplexType>();
            listComplexTypes = new Dictionary<string, List<XmlSchemaComplexType>>();
            sameTypes = new Dictionary<string, string>();
            var rootTypes = new Dictionary<string, bool>();
            tblList = new Dictionary<string, bool>();

            foreach (XmlSchema schema in ss.Schemas())
            {
                var list = new List<XmlSchemaComplexType>();
                if (!listComplexTypes.ContainsKey(schema.TargetNamespace))
                    listComplexTypes.Add(schema.TargetNamespace, list);
                else
                    list = listComplexTypes[schema.TargetNamespace];

                foreach (DictionaryEntry sce in schema.Elements)
                {
                    var xse = (XmlSchemaElement)sce.Value;
                    rootTypes.Add(xse.SchemaTypeName.ToString(), true);
                }

                foreach (XmlSchemaType type in schema.SchemaTypes.Values)
                {
                    if (type is XmlSchemaComplexType)
                    {
                        XmlSchemaComplexType ct = type as XmlSchemaComplexType;
                        complexTypes.Add(ct.QualifiedName.ToString(), ct);
                        list.Add(ct);
                        ParseGroupType(ct, list);
                    }
                    else if (type is XmlSchemaSimpleType)
                    {
                        XmlSchemaSimpleType st = type as XmlSchemaSimpleType;
                        simpleTypes.Add(st.QualifiedName.ToString(), st);
                    }
                }
            }

            int i = 1;
            var namesDict = new Dictionary<string, List<Tuple<string, string>>>();
            foreach (var ns in listComplexTypes.Keys)
            {
                if (listComplexTypes[ns].Count == 0)
                    continue;

                string prefix = "T" + i;
                if (nsMap != null)
                {
                    if (!nsMap.ContainsKey(ns))
                        throw new Exception("Prefix for namespace not specified: " + ns);
                    prefix = nsMap[ns];
                }

                foreach (var ct in listComplexTypes[ns])
                {
                    if (ct.Name == null)
                        continue;
                    if (!namesDict.ContainsKey(ct.Name))
                        namesDict.Add(ct.Name, new List<Tuple<string, string>>());
                    namesDict[ct.Name].Add(Tuple.Create(prefix, ct.QualifiedName.ToString()));
                }
            }
            foreach (var k in namesDict)
            {
                if (k.Value.Count > 1)
                {
                    foreach (var v in k.Value)
                        sameTypes.Add(v.Item2, v.Item1 + k.Key);
                }
            }

            _opt = opt;

            var res = new List<string>();
            foreach (var ns in listComplexTypes.Keys)
            {
                if (listComplexTypes[ns].Count == 0)
                    continue;

                string prefix = "ns.nms";
                string prefix2 = "";
                if (nsMap != null)
                {
                    if (!nsMap.ContainsKey(ns))
                        throw new Exception("Prefix for namespace not specified: " + ns);
                    prefix = "ns." + nsMap[ns];
                    prefix2 = nsMap[ns];
                }

                string namesp = opt.CSharpNamespace ?? "Yournamespace";
                var sb = new StringBuilder();
                sb.Append("\n\n *** " + ns + " *** \n\n");
                sb.Append(@"
using Xml;
using System;
using System.Collections.Generic;
using System.Xml.Linq;" +
(opt.StoreDB ? "\nusing QueryGenerator;" : "") +
(opt.ReadDB ? "\nusing Db;\nusing System.Data.Common;" : "") +
@"

namespace " + namesp + @".AF.Kps
{");
                foreach (var ct in listComplexTypes[ns])
                {
                    string ann = getAnnotation(ct.Annotation);
                    if (string.IsNullOrEmpty(ann) && complexTypeElem.ContainsKey(ct))
                        ann = getAnnotation(complexTypeElem[ct].Annotation);

                    sb.Append("\n" + S1 + "//" + ann + "\n");
                    sb.Append(S1 + "public class " + className(ct) + " : ");
                    var ignoredNames = new Dictionary<string, bool>();
                    wasParent = false;
                    if (ct.BaseXmlSchemaType.QualifiedName.ToString() != "http://www.w3.org/2001/XMLSchema:anyType")
                    {
                        var ctBase = className(ct.BaseXmlSchemaType);
                        if (!string.IsNullOrEmpty(ctBase))
                        {
                            sb.Append(ctBase + ", ");
                            wasParent = true;
                        }
                        getIgnoredNames(ct, ignoredNames);
                    }
                    bool rootType = rootTypes.ContainsKey(ct.QualifiedName.ToString());
                    sb.Append("IXml" + (opt.ReadDB ? ", IRow" : "") + (rootType && opt.StoreDB ? ", IQObject" : "") + "\n" + S1 + "{");

                    var particle = ct.ContentTypeParticle;
                    if (particle.ToString().EndsWith("EmptyParticle") || particle is XmlSchemaGroupBase)
                    //xs:all, xs:choice, xs:sequence
                    {
                        sb.Append("\n");

                        pr = new StringBuilder();
                        pu = new StringBuilder();
                        fb = new StringBuilder();
                        fx = new StringBuilder();
                        tx = new StringBuilder();
                        si = new StringBuilder();
                        si1 = new StringBuilder();
                        si2 = new StringBuilder();
                        si3 = new StringBuilder();
                        fldList = new Dictionary<string, bool>();

                        if (opt.ReadDB)
                            fb.Append(S + "public void Init(DbDataReader r, Dictionary<string, int> columns)\n" + S + "{\n");
                        fx.Append(S + "public " + (wasParent ? "new " : "") + "void Init(XElement r)\n" + S + "{\n");
                        if (wasParent)
                            fx.Append(S2 + "base.Init(r);\n\n");
                        tx.Append(S + "public " + (wasParent ? "new " : "") + "XElement ToXElement(XName name, Namespaces ns)\n" + S + "{\n");
                        if (wasParent)
                            tx.Append(S2 + "var r = base.ToXElement(name, ns);\n\n");
                        else
                            tx.Append(S2 + "var r = new XElement(name);\n\n");

                        if (rootType && opt.StoreDB)
                        {
                            si.Append(S + "public void StoreInfo(QData data)\n" + S + "{\n");
                            var cmt = getAnnotation(ct.Annotation).Replace("'", "\"").Replace("\"", "\\\"");
                            si.Append(S2 + "var qt = new QTable { Name = \"" + opt.StoreDBPrefix + "_" + shortName(prefix2) + "\", Comment = \"" + cmt + "\", Pk = \"Id\", PkComment = \"Идентификатор\" };\n");
                            si.Append(S2 + className(ct) + ".StoreInfo(qt, null, \"\", null, \"" + opt.StoreDBPrefix + "_" + shortName(prefix2) + "_\", null, data);\n" + S + "}\n\n");
                        }

                        si.Append(S + "internal static " + (wasParent ? "new " : "") + "void StoreInfo(QTable qt, QHierarchy h, string prefix, string comment, string tab_prefix, string tab_comment, QData data)\n" + S + "{\n");

                        wasVar = false;
                        wasList = false;
                        wasItem = false;
                        wasLongB = false;
                        wasLong = false;

                        if (particle is XmlSchemaGroupBase)
                        {
                            XmlSchemaGroupBase baseParticle = particle as XmlSchemaGroupBase;

                            string gName = groupName(baseParticle.ToString());
                            int pos1 = 0;
                            int pos2 = 0;
                            if (gName != "sequence")
                            {
                                pos1 = pu.Length;
                                if (pu.Length > 0)
                                    pu.Append("\n");
                                pu.Append(S + "//" + gName + "\n");
                                pos2 = pu.Length;
                            }
                            bool isChoice = (groupName(baseParticle.ToString()) == "choice");
                            ParseGroup(ignoredNames, prefix, baseParticle, isChoice);
                            if (gName != "sequence")
                            {
                                if (pu.Length == pos2)
                                    pu.Length = pos1;
                                else
                                    pu.Append(S + "//end " + gName + "\n\n");
                            }
                        }

                        if (opt.ReadDB)
                            fb.Append(S + "}\n\n");
                        fx.Append(S + "}\n");
                        tx.Append("\n" + S2 + "return r;\n" + S + "}\n");

                        string si1_str = "data.AddInfo(qt, h);\n";
                        if (si1.Length > 1)
                        {
                            si1.Length = si1.Length - 2;
                            si1_str = "var prf = (prefix != null && prefix.Length > 10 ? prefix.Substring(0, 5) + prefix.Substring(prefix.Length - 5) : prefix);\n";
                            si1_str += S2 + "data.AddInfo(qt, h,\n" + si1.ToString() + ");\n";
                        }
                        if (wasParent)
                            si1_str += S2 + className(ct.BaseXmlSchemaType) + ".StoreInfo(qt, h, prefix, comment, tab_prefix, tab_comment, data);\n";
                        si1_str += "\n";
                        string si_str = S2 + si1_str + si2.ToString() + "\n" + si3.ToString();
                        si_str = si_str.TrimEnd(' ', '\n');
                        si.Append(si_str + "\n" + S + "}\n");

                        sb.Append(pr);
                        if (pr.Length > 0)
                            sb.Append("\n");

                        if (pu.Length > 2 && pu[pu.Length - 1] == '\n' && pu[pu.Length - 2] == '\n')
                            pu.Length--;
                        sb.Append(pu);
                        if (pu.Length > 0)
                            sb.Append("\n");

                        if (opt.ReadDB)
                            sb.Append(fb);
                        sb.Append(fx);
                        sb.Append("\n");
                        sb.Append(tx);

                        if (opt.StoreDB)
                        {
                            sb.Append("\n");
                            sb.Append(si);
                        }
                    }

                    sb.Append(S1 + "}\n");
                }
                sb.Append("\n}\n");
                res.Add(sb.ToString());

                string path = Path.Combine(gDir, fileSet[ns]);
                log.Debug("write content to " + path + " file");
                for (int j = 0; j < 10; j++)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                            sw.Write(sb.ToString().Substring(sb.ToString().IndexOf("using ")));
                    }
                    catch(Exception)
                    {
                        if (j == 9)
                            throw;
                        System.Threading.Thread.Sleep(500); //wait for file close from another process
                        continue;
                    }
                    break;
                }
            }
            return res;
        }

        private void ParseGroupType(XmlSchemaComplexType ct, List<XmlSchemaComplexType> list)
        {
            var particle = ct.ContentTypeParticle;
            if (particle.ToString().EndsWith("EmptyParticle") || particle is XmlSchemaGroupBase)
            //xs:all, xs:choice, xs:sequence
            {
                if (particle is XmlSchemaGroupBase)
                {
                    XmlSchemaGroupBase baseParticle = particle as XmlSchemaGroupBase;
                    ParseGroupType(baseParticle, list);
                }
            }
        }

        private void ParseGroupType(XmlSchemaGroupBase baseParticle, List<XmlSchemaComplexType> list)
        {
            foreach (XmlSchemaParticle subParticle in baseParticle.Items)
            {
                if (subParticle is XmlSchemaGroupBase)
                {
                    string gName = groupName(subParticle.ToString());
                    XmlSchemaGroupBase baseParticle2 = subParticle as XmlSchemaGroupBase;
                    ParseGroupType(baseParticle2, list);
                }
                else if (subParticle is XmlSchemaElement)
                {
                    XmlSchemaElement elem = subParticle as XmlSchemaElement;
                    if (elem.SchemaType != null && elem.SchemaType is XmlSchemaComplexType)
                    {
                        var type = (XmlSchemaComplexType)elem.SchemaType;
                        if (!complexTypeElemNames.ContainsKey(elem.Name) && !complexTypeElem.ContainsKey(type))
                        {
                            list.Add(type);
                            complexTypeElem.Add(type, elem);
                            complexTypeElemNames.Add(elem.Name, true);
                        }
                        ParseGroupType(type, list);
                    }
                }
            }
        }

        private void ParseGroup(Dictionary<string, bool> ignoredNames, string prefix, XmlSchemaGroupBase baseParticle, bool isChoice)
        {
            foreach (XmlSchemaParticle subParticle in baseParticle.Items)
            {
                if (subParticle is XmlSchemaGroupBase)
                {
                    int pos1 = pu.Length;
                    if (pu.Length > 0)
                        pu.Append("\n");
                    string gName = groupName(subParticle.ToString());
                    pu.Append(S + "//" + gName + "\n");
                    int pos2 = pu.Length;
                    XmlSchemaGroupBase baseParticle2 = subParticle as XmlSchemaGroupBase;
                    isChoice = isChoice || (groupName(baseParticle2.ToString()) == "choice");
                    ParseGroup(ignoredNames, prefix, baseParticle2, isChoice);
                    if (pu.Length == pos2)
                        pu.Length = pos1;
                    else
                        pu.Append(S + "//end " + gName + "\n\n");
                }
                else if (subParticle is XmlSchemaElement)
                {
                    XmlSchemaElement elem = subParticle as XmlSchemaElement;
                    if (ignoredNames.ContainsKey(elem.QualifiedName.Name))
                        continue;
                    if (wasLong)
                        fx.Append("\n");
                    var cmt = getAnnotation(elem.Annotation).Replace("'", "\"").Replace("\"", "\\\"");
                    var ename = elem.Name.Replace('-', '_');

                    var enameS = shortName(ename);

                    var enameO = elem.Name;
                    if (complexTypes.ContainsKey(elem.SchemaTypeName.ToString()) || elem.ElementSchemaType is XmlSchemaComplexType)
                    {
                        var type = (XmlSchemaComplexType)elem.ElementSchemaType ?? complexTypes[elem.SchemaTypeName.ToString()];
                        var typeName = className(type);
                        if (typeName == null && type.Datatype != null && type.Datatype.ValueType != null)
                            typeName = fixBaseType(type.Datatype.ValueType.Name);
                        if (typeName == null)
                            typeName = ename + "Type";
                        var typeName2 = typeName;
                        if (typeName2.EndsWith("Type"))
                            typeName2 = typeName2.Substring(0, typeName2.Length - 4);
                        if (elem.MaxOccurs > 1)
                        {
                            si3.Append(S2 + typeName + ".StoreInfo(new QTable { Name = (tab_prefix.Length > 23 ? tab_prefix.Substring(0, 23) : tab_prefix) + \"" + (enameS.Length > 3 ? enameS.Substring(0, 3) : enameS) + "\", Comment = (tab_comment != null ? tab_comment + \": \" : \"\") + \"" + cmt + "\", Pk = \"Id\", PkComment = \"Идентификатор\", Fk = \"IdFk\", FkComment = \"Идентификатор родительской записи\" },\n");
                            si3.Append(S3 + "new QHierarchy(\"" + ename + "\", QHType.List, h), null, null, tab_prefix + \"" + enameS + "\", (tab_comment != null ? tab_comment + \": \" : \"\") + \"" + cmt + "\", data);\n");
                            pr.Append(S + "private List<" + typeName + "> " + uname(ename) + " = new List<" + typeName + ">();\n");
                            pu.Append(S + "public List<" + typeName + "> " + ename + " { get { return " + uname(ename) + "; } } //");

                            if (wasItem && !wasLong)
                                fx.Append("\n");
                            fx.Append(S2 + ename + ".Clear();\n");
                            fx.Append(S2 + (wasList ? "" : "var ") + "list = XmlParser.Elements(r, \"" + enameO + "\");\n");
                            wasList = true;
                            fx.Append(S2 + "foreach (var i in list)\n" + S2 + "{\n");
                            fx.Append(S3 + "var o = new " + typeName + "();\n");
                            fx.Append(S3 + "o.Init(i);\n");
                            fx.Append(S3 + ename + ".Add(o);\n" + S2 + "}\n");
                            wasLong = true;

                            tx.Append(S2 + "foreach (var i in " + ename + ")\n");
                            tx.Append(S3 + "r.Add(i.ToXElement(" + prefix + " + \"" + enameO + "\", ns));\n");
                        }
                        else
                        {
                            si2.Append(S2 + typeName + ".StoreInfo(qt, new QHierarchy(\"" + ename + "\", QHType.Member, h), prefix + \"" + enameS + "\", (comment != null ? comment + \": \" : \"\") + \"" + cmt + "\", tab_prefix + \"" + enameS + "\", (tab_comment != null ? tab_comment + \": \" : \"\") + \"" + cmt + "\", data);\n");
                            if (elem.MinOccurs == 0 || isChoice)
                            {
                                pu.Append(S + "public " + typeName + " " + ename + " { get; set; } //");

                                if (wasLongB)
                                    fb.Append("\n");
                                fb.Append(S2 + ename + " = new " + typeName + "();\n");
                                fb.Append(S2 + ename + ".Init(r, columns);\n");
                                wasLongB = true;

                                if (wasItem && !wasLong)
                                    fx.Append("\n");
                                fx.Append(S2 + (wasVar ? "" : "var ") + "e = XmlParser.Element(r, \"" + enameO + "\", false);\n");
                                wasVar = true;
                                fx.Append(S2 + "if (e != null)\n" + S2 + "{\n");
                                fx.Append(S3 + ename + " = new " + typeName + "();\n");
                                fx.Append(S3 + ename + ".Init(e);\n" + S2 + "}\n");
                                wasLong = true;

                                tx.Append(S2 + "if (" + ename + " != null)\n");
                                tx.Append(S3 + "r.Add(" + ename + ".ToXElement(" + prefix + " + \"" + enameO + "\", ns));\n");
                            }
                            else if (elem.MinOccurs == 1)
                            {
                                pr.Append(S + "private " + typeName + " " + uname(ename) + " = new " + typeName + "();\n");
                                pu.Append(S + "public " + typeName + " " + ename + " { get { return " + uname(ename) + "; } } //");

                                fb.Append(S2 + ename + ".Init(r, columns);\n");
                                fx.Append(S2 + ename + ".Init(XmlParser.Element(r, \"" + enameO + "\"));\n");
                                tx.Append(S2 + "r.Add(" + ename + ".ToXElement(" + prefix + " + \"" + enameO + "\", ns));\n");
                                wasLong = false;
                            }
                        }
                        if (elem.MinOccurs == 0)
                            pu.Append("optional, ");
                        if (elem.MaxOccurs > 1 && elem.MaxOccurs < 1000)
                            pu.Append("maxOccurs: " + elem.MaxOccurs + ", ");
                    }
                    else if (simpleTypes.ContainsKey(elem.SchemaTypeName.ToString()) || elem.ElementSchemaType is XmlSchemaSimpleType)
                    {
                        var type = (XmlSchemaSimpleType)elem.ElementSchemaType ?? simpleTypes[elem.SchemaTypeName.ToString()];
                        var info = getSimpleTypeInfo(type);
                        if (elem.MaxOccurs > 1)
                        {
                            pr.Append(S + "private List<" + info.BaseType + "> " + uname(ename) + " = new List<" + info.BaseType + ">();\n");
                            pu.Append(S + "public List<" + info.BaseType + "> " + ename + " { get { return " + uname(ename) + "; } } //");

                            if (info.BaseType == "string")
                            {
                                if (wasItem && !wasLong)
                                    fx.Append("\n");
                                fx.Append(S2 + ename + ".Clear();\n");
                                fx.Append(S2 + (wasList ? "" : "var ") + "list = XmlParser.Elements(r, \"" + enameO + "\");\n");
                                wasList = true;
                                fx.Append(S2 + "foreach (var i in list)\n");
                                fx.Append(S3 + ename + ".Add(i.Value);\n");
                                wasLong = true;

                                tx.Append(S2 + "foreach (var i in " + ename + ")\n");
                                tx.Append(S3 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", i));\n");
                            }
                        }
                        else
                        {
                            string baseType = info.BaseType;
                            string qType = "String";
                            int maxLen = info.MaxLen;
                            if (wasLongB)
                                fb.Append("\n");
                            if (elem.MinOccurs == 0 || isChoice)
                            {
                                if (info.BaseType == "string")
                                {
                                    if (maxLen == -1)
                                        maxLen = 100;
                                    fb.Append(S2 + ename + " = Util.ToStr(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValue(r, \"" + enameO + "\", false);\n");
                                    tx.Append(S2 + "if (!string.IsNullOrEmpty(" + ename + "))\n");
                                    tx.Append(S3 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", " + ename + "));\n");
                                }
                                else if (info.BaseType == "DateTime")
                                {
                                    qType = "Date";
                                    baseType += "?";
                                    fb.Append(S2 + ename + " = Util.ToDateNull(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValueDateNull(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "if (" + ename + " != null)\n");
                                    tx.Append(S3 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", XmlParser.Date" + (info.TypeCode == "DateTime" ? "Time" : "") + "2Str(" + ename + ".Value)));\n");
                                }
                                else if (info.BaseType == "decimal")
                                {
                                    qType = "Number";
                                    baseType += "?";
                                    fb.Append(S2 + ename + " = Util.ToDecimalNull(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValueDecimalNull(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "if (" + ename + " != null)\n");
                                    tx.Append(S3 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", XmlParser.Decimal2Str(" + ename + ".Value)));\n");
                                }
                                else if (info.BaseType == "int")
                                {
                                    qType = "Number";
                                    baseType += "?";
                                    fb.Append(S2 + ename + " = Util.ToIntNull(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValueIntNull(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "if (" + ename + " != null)\n");
                                    tx.Append(S3 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", XmlParser.Int2Str(" + ename + ".Value)));\n");
                                }
                                else if (info.BaseType == "bool")
                                {
                                    baseType = "string";
                                    maxLen = 1;
                                    fb.Append(S2 + ename + " = Util.ToStr(r[\"" + enameO + "\"]) == \"\" ? null : Util.ToStr(r[\"" + enameO + "\"]) == \"Да\";\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValueBool(r, \"" + enameO + "\", false);\n");
                                    tx.Append(S2 + "if (!string.IsNullOrEmpty(" + ename + "))\n");
                                    tx.Append(S3 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", " + ename + "));\n");
                                }
                            }
                            else if (elem.MinOccurs == 1)
                            {
                                if (info.BaseType == "string")
                                {
                                    if (maxLen == -1)
                                        maxLen = 100;
                                    fb.Append(S2 + ename + " = Util.ToStr(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValue(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", " + ename + "));\n");
                                }
                                else if (info.BaseType == "DateTime")
                                {
                                    qType = "Date";
                                    fb.Append(S2 + ename + " = Util.ToDate(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = (DateTime)XmlParser.Element(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", XmlParser.Date" + (info.TypeCode == "DateTime" ? "Time" : "") + "2Str(" + ename + ")));\n");
                                }
                                else if (info.BaseType == "decimal")
                                {
                                    qType = "Number";
                                    fb.Append(S2 + ename + " = Util.ToDecimal(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = (decimal)XmlParser.Element(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", XmlParser.Decimal2Str(" + ename + ")));\n");
                                }
                                else if (info.BaseType == "int")
                                {
                                    qType = "Number";
                                    fb.Append(S2 + ename + " = Util.ToInt(r[\"" + enameO + "\"]);\n");
                                    fx.Append(S2 + ename + " = (int)XmlParser.Element(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", XmlParser.Int2Str(" + ename + ")));\n");
                                }
                                else if (info.BaseType == "bool")
                                {
                                    baseType = "string";
                                    info.MaxLen = 1;
                                    fb.Append(S2 + ename + " = Util.ToStr(r[\"" + enameO + "\"]) == \"Да\";\n");
                                    fx.Append(S2 + ename + " = XmlParser.ElementValueBool(r, \"" + enameO + "\");\n");
                                    tx.Append(S2 + "r.Add(new XElement(" + prefix + " + \"" + enameO + "\", " + ename + "));\n");
                                }
                            }

                            string enameF = ename;
                            if (enameF.Length > 15)
                                enameF = shortName(ename);
                            enameF = prohibNames(enameF);
                            enameF = uniqueName(enameF, fldList);
                            if (_opt.ExactDBNames)
                                enameF = ename;
                            si1.Append(S3 + "new QField { " + (enameF.ToLower() == ename.ToLower() ? "Name = \"" + ename + "\"" : "Name = \"" + enameF + "\", NameCs = \"" + ename + "\"") +", Type = QType." + qType + (maxLen != -1 ? ", Size = " + maxLen : "") + ", Prefix = " + (_opt.ExactDBNames ? "null" : "prf") + ", Comment = " + (_opt.ExactDBNames ? "" : "(comment != null ? comment + \": \" : \"\") + ") + "\"" + cmt + "\" },\n");
                            pu.Append(S + "public " + baseType + " " + ename + " { get; set; } //");
                            wasLong = false;
                        }
                        if (elem.MinOccurs == 0)
                            pu.Append("optional, ");
                        if (!string.IsNullOrEmpty(info.Pattern))
                            pu.Append("pattern: " + info.Pattern + ", ");
                        if (info.MinLen != -1)
                            pu.Append("minLen: " + info.MinLen + ", ");
                        if (info.MaxLen != -1)
                            pu.Append("maxLen: " + info.MaxLen + ", ");
                    }
                    else
                    {
                        throw new Exception("Type not found: " + elem.SchemaTypeName.ToString());
                    }
                    wasItem = true;
                    pu.Append(getAnnotation(elem.Annotation) + "\n");
                }
            }
        }

        private string prohibNames(string enameF)
        {
            var enameF1 = enameF.ToLower();
            if (enameF1 == "level")
                enameF = "Lvl";
            else if (enameF1 == "number")
                enameF = "Num";
            else if (enameF1 == "id")
                enameF = "Idt";
            else if (enameF1 == "fkid")
                enameF = "FkIdt";
            else if (enameF1 == "on")
                enameF = "ONf";
            else if (enameF1 == "share")
                enameF = "Shar";
            return enameF;
        }

        private string shortName(string ename)
        {
            var enameS = "";
            foreach (var c in ename)
            {
                if (c.ToString() == c.ToString().ToUpper())
                    enameS += c.ToString();
            }
            if (enameS.Length < 2 && ename.Length > 2)
                enameS = ename.Substring(0, 3);

            return enameS;
        }

        private string uniqueName(string tab, Dictionary<string, bool> dict)
        {
            string tab1 = tab;
            for (int i = 1; i < 10 && dict.ContainsKey(tab1.ToLower()); i++)
                tab1 = tab + i.ToString();
            dict.Add(tab1.ToLower(), true);

            return tab1;
        }

        private string className(XmlSchemaType type)
        {
            if (sameTypes.ContainsKey(type.QualifiedName.ToString()))
                return sameTypes[type.QualifiedName.ToString()];

            if (type.Name == null && complexTypeElem.ContainsKey(type))
                return complexTypeElem[type].Name + "Type";

            return type.Name;
        }

        private string groupName(string s)
        {
            if (s.StartsWith("System.Xml.Schema.XmlSchema"))
                s = s.Substring("System.Xml.Schema.XmlSchema".Length).ToLower();
            return s;
        }

        private void getIgnoredNames(XmlSchemaComplexType ct, Dictionary<string, bool> ignoredNames)
        {
            var childName = ct.BaseXmlSchemaType.QualifiedName.ToString();
            if (childName != "http://www.w3.org/2001/XMLSchema:anyType" && complexTypes.ContainsKey(childName))
            {
                //getIgnoredNames(childName, complexTypes, ignoredNames);
                var ctChild = complexTypes[childName];

                var particle = ctChild.ContentTypeParticle;
                XmlSchemaGroupBase baseParticle = particle as XmlSchemaGroupBase;

                foreach (XmlSchemaParticle subParticle in baseParticle.Items)
                    AddIgnoredNames(ignoredNames, subParticle);
            }
        }

        private void AddIgnoredNames(Dictionary<string, bool> ignoredNames, XmlSchemaParticle subParticle)
        {
            if (subParticle is XmlSchemaGroupBase)
            {
                XmlSchemaGroupBase group = subParticle as XmlSchemaGroupBase;

                foreach (XmlSchemaParticle particle in group.Items)
                {
                    AddIgnoredNames(ignoredNames, particle);
                }
            }
            else if (subParticle is XmlSchemaElement)
            {
                XmlSchemaElement elem = subParticle as XmlSchemaElement;
                ignoredNames[elem.QualifiedName.Name] = true;
            }
        }

        private SimpleTypeInfo getSimpleTypeInfo(XmlSchemaSimpleType type)
        {
            string baseType = null;
            string typeCode = null;
            string pattern = null;
            int minLen = -1;
            int maxLen = -1;

            while (type != null)
            {
                string baseType2 = type.Datatype.ValueType.Name;
                string typeCode2 = type.TypeCode.ToString();
                if (typeCode2 == "GYear" || typeCode2 == "Time")
                    baseType2 = "String";
                string pattern2 = getPattern(type);
                int minLen2 = getMinLength(type);
                int maxLen2 = getMaxLength(type);

                if (baseType2 != null && baseType != baseType2)
                    baseType = baseType2;
                if (typeCode2 != null && typeCode != typeCode2)
                    typeCode = typeCode2;
                if (pattern2 != null && pattern == null)
                    pattern = pattern2;
                if (minLen2 != -1 && (minLen2 > minLen || minLen == -1))
                    minLen = minLen2;
                if (maxLen2 != -1 && (maxLen2 < maxLen || maxLen == -1))
                    maxLen = maxLen2;

                string baseName = type.BaseXmlSchemaType.QualifiedName.ToString();
                if (simpleTypes.ContainsKey(baseName))
                    type = simpleTypes[baseName];
                else
                    type = null;
            }

            return new SimpleTypeInfo { BaseType = fixBaseType(baseType), TypeCode = typeCode, Pattern = pattern, MinLen = minLen, MaxLen = maxLen };
        }

        private string fixBaseType(string baseType)
        {
            if (baseType == "String")
                return "string";
            if (baseType == "Decimal")
                return "decimal";
            if (baseType == "Boolean")
                return "bool";
            if (baseType == "Int32")
                return "int";
            if (baseType == "DateTime")
                return "DateTime";
            log.Debug("!!! unsupported type: " + baseType);
            return baseType;
        }

        class SimpleTypeInfo
        {
            public string BaseType { get; set; }
            public string TypeCode { get; set; }
            public string Pattern { get; set; }
            public int MinLen { get; set; }
            public int MaxLen { get; set; }
        }

        private string getPattern(XmlSchemaSimpleType simpleType)
        {
            var restriction = simpleType.Content as XmlSchemaSimpleTypeRestriction;
            if (restriction == null)
                return null;

            string result = "";
            foreach (XmlSchemaObject facet in restriction.Facets)
            {
                if (facet is XmlSchemaPatternFacet)
                    result = ((XmlSchemaFacet)facet).Value;
            }
            return result;
        }

        private int getMaxLength(XmlSchemaSimpleType simpleType)
        {
            var restriction = simpleType.Content as XmlSchemaSimpleTypeRestriction;
            if (restriction == null)
                return -1;

            int result = -1;
            foreach (XmlSchemaObject facet in restriction.Facets)
            {
                if (facet is XmlSchemaMaxLengthFacet)
                    result = int.Parse(((XmlSchemaFacet)facet).Value);
            }
            return result;
        }

        private int getMinLength(XmlSchemaSimpleType simpleType)
        {
            var restriction = simpleType.Content as XmlSchemaSimpleTypeRestriction;
            if (restriction == null)
                return -1;

            int result = -1;
            foreach (XmlSchemaObject facet in restriction.Facets)
            {
                if (facet is XmlSchemaMinLengthFacet)
                    result = int.Parse(((XmlSchemaFacet)facet).Value);
            }
            return result;
        }

        private string uname(string name)
        {
            return "_" + name;// name[0].ToString().ToLower() + name.Substring(1);
        }

        private string getAnnotation(XmlSchemaAnnotation an)
        {
            string str = "";
            if (an != null && an.Items != null)
            {
                foreach (XmlSchemaObject annotation in an.Items)
                {
                    if (annotation is XmlSchemaDocumentation)
                    {
                        foreach (XmlNode doc in ((XmlSchemaDocumentation)annotation).Markup)
                        {
                            if (str.Length > 0)
                                str += " ";
                            str += doc.InnerText;
                        }
                    }
                }
            }
            str = str.Replace('\n', ' ').Replace('\t', ' ');
            while (str.IndexOf("  ") != -1)
                str = str.Replace("  ", " ");
            if (str.EndsWith(" "))
                str = str.Substring(0, str.Length - 1);
            return str;
        }
    }
}
