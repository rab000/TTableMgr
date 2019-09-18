
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Globalization;
using System.Text;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using ZeroFormatter;

public class CSV2Csharp
{
    /// <summary>
    /// 分隔符
    /// </summary>
    private static readonly string SEPARATOR = ",";

    /// <summary>
    /// 生成类名空间
    /// </summary>
    private static readonly string NAMESPACE = "MergeTown";

    private static readonly string ARRAY_EXTENSION = "_list";

    /// <summary>
    /// 原始csv路径
    /// </summary>
    private static string Input_CSV = "Assets/InputCsv";

    /// <summary>
    /// 输出csharp路径
    /// </summary>
    private static string Output_CSharp = "Assets/OutputCSharp";

    /// <summary>
    /// 输出zeroFormatter文件路径
    /// zeroFormatter必须注册类才能使用
    /// 这里生成注册代码
    /// </summary>
    private static string Output_ZeromatterAdd = "Assets/OutputZeroFomatter/ZeroFormatterGenerated.Additional.cs";

    /// <summary>
    /// ZeroMatter生成文件夹
    /// </summary>
    private static string Output_Zeromatter = "Assets/OutputZeroFomatter";

    /// <summary>
    /// 生成二进制数据表位置
    /// </summary>
    private static  string Output_Bytes = "Assets/OutputBytes";

    /// <summary>
    /// zeroFomatter工具路径，用于生成ZeroFormatterGenerated.cs
    /// </summary>
    private static string Path_ZeroFomatterTool
    {
        get
        {
            return Path.GetDirectoryName(Application.dataPath)+"/ZfcTools/NZfc.bat";
        }
    }

    private static readonly Regex regex = new Regex("(?<=^|,)[^\\\"]*?(?=,|$)|(?<=^|,\\\")(?:(\\\"\\\")?[^\\\"]*?)*(?=\\\",?|$)", RegexOptions.ExplicitCapture);

    [MenuItem("TableTools/清理")]
    private static void Clear()
    {
        DeleteFilesInFolder(ChangeToAbsolutePath(Output_Bytes));

        DeleteFilesInFolder(ChangeToAbsolutePath(Output_CSharp));

        DeleteFilesInFolder(ChangeToAbsolutePath(Output_Zeromatter));

        AssetDatabase.Refresh();

    }

    /// <summary>
    /// 生成excel为csharp文件
    /// </summary>
    [MenuItem("TableTools/导出类")]
    public static void GenerateScripts()
    {
        List<string> allKeyTypes = new List<string>();
        List<string> allClassNames = new List<string>();
        List<string> dataKeyTypes = new List<string>();
        List<string> dataClassNames = new List<string>();
        
        //从csv生成所有cs
        GenerateCSVScripts(Input_CSV, dataKeyTypes, dataClassNames);

        //生成DataMgr.cs
        GenerateTableMgrScript(dataKeyTypes, dataClassNames);

        allKeyTypes.AddRange(dataKeyTypes);        

        allClassNames.AddRange(dataClassNames);

        AssetDatabase.Refresh();

        //生成ZeroFormatterGenerated
        GenerateZeroFomatter();

        //生成ZeroFormatter需要的文件
        GenerateFormatterScript(allKeyTypes, allClassNames);

        AssetDatabase.Refresh();

    }

    private static void GenerateCSVScripts(string path, List<string> keyTypes, List<string> classNames)
    {

        DirectoryInfo directoryInfo = new DirectoryInfo(path);
        FileInfo[] csvFiles = directoryInfo.GetFiles("*", SearchOption.AllDirectories);
        foreach (FileInfo fileInfo in csvFiles)
        {
            if (fileInfo.FullName.EndsWith(".meta", true, System.Globalization.CultureInfo.CurrentCulture))
            {
                continue;
            }

            string className = Path.GetFileNameWithoutExtension(fileInfo.Name);
            string keyType;
            if (GenerateCSVScript(fileInfo, className, out keyType))
            {
                keyTypes.Add(keyType);
                classNames.Add(className);
            }
        }
    }

    private static bool GenerateCSVScript(FileInfo fileInfo, string className, out string keyType)
    {
        using (StreamReader streamReader = fileInfo.OpenText())
        {
            List<int> invalidColumns = new List<int>();
            List<string> comments = null;
            List<string> fieldNams = null;
            List<string> fieldTypes = null;

            string content = streamReader.ReadToEnd();
            string[] lines = content.Split(new string[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                keyType = null;
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                string line = lines[i];
                switch (i)
                {
                    case 0:
                        comments = ReadComments(line, invalidColumns);
                        break;

                    case 1:
                        fieldNams = ReadFieldNames(line, invalidColumns);
                        break;

                    case 2:
                        fieldTypes = ReadFieldTypes(line, invalidColumns);
                        break;
                }
            }

            keyType = fieldTypes[0];
            WriteCSVScriptFile(className, comments, fieldTypes, fieldNams);
            return true;
        }
    }

    private static void WriteCSVScriptFile(string className, List<string> comments, List<string> fieldTypes, List<string> fieldNams)
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("// WARNING: Do not modify! Generated file.");
        stringBuilder.AppendLine("using System;");
        stringBuilder.AppendLine("using ZeroFormatter;");
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("namespace "+ NAMESPACE);
        stringBuilder.AppendLine("{");
        stringBuilder.AppendLine("\t[ZeroFormattable]");
        stringBuilder.AppendLine($"\tpublic class {className}");
        stringBuilder.AppendLine("\t{");
        for (int i = 0; i < fieldNams.Count; i++)
        {
            string comment = comments[i];
            string fieldName = fieldNams[i];
            string fieldType = fieldTypes[i];
            stringBuilder.AppendLine($"\t\t//{comment}");
            stringBuilder.AppendLine($"\t\t[Index({i})]");
            stringBuilder.AppendLine($"\t\tpublic virtual {fieldType} {fieldName} " + "{ get; set; }");
        }
        stringBuilder.AppendLine("\t}");
        stringBuilder.AppendLine("}");

        StreamWriter writer = File.CreateText($"{Output_CSharp}/{className}.cs");
        writer.Write(stringBuilder.ToString());
        writer.Flush();
        writer.Close();
    }

    private static void GenerateFormatterScript(List<string> keyTypes, List<string> classNames)
    {
        StringBuilder stringBuilder = new StringBuilder();

        stringBuilder.AppendLine("namespace ZeroFormatter");
        stringBuilder.AppendLine("{");
        stringBuilder.AppendLine("\tusing System;");
        stringBuilder.AppendLine("\tusing System.Collections.Generic;");
        stringBuilder.AppendLine("\tusing global::ZeroFormatter.Formatters;");
        stringBuilder.AppendLine("\tusing global::"+ NAMESPACE+"; ");
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("\tpublic static partial class ZeroFormatterInitializer");
        stringBuilder.AppendLine("\t{");
        stringBuilder.AppendLine("\t\t[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
        stringBuilder.AppendLine("\t\tpublic static void HandRegisterd()");
        stringBuilder.AppendLine("\t\t{");
        stringBuilder.AppendLine("\t\t\tZeroFormatterInitializer.Register();");
        for (int i = 0; i < classNames.Count; i++)
        {
            string className = classNames[i];
            stringBuilder.AppendLine($"\t\t\tFormatter.RegisterArray<DefaultResolver, {className}>();");
        }
        stringBuilder.AppendLine();
        for (int i = 0; i < classNames.Count; i++)
        {
            string className = classNames[i];
            string keyType = keyTypes[i];
            stringBuilder.AppendLine($"\t\t\tFormatter.RegisterDictionary<DefaultResolver, {keyType}, {className}>();");
        }
        stringBuilder.AppendLine("\t\t}");

        stringBuilder.AppendLine();
        stringBuilder.AppendLine("\t\tpublic static byte[] Serialize(object data)");
        stringBuilder.AppendLine("\t\t{");
        stringBuilder.AppendLine("\t\t\tType type = data.GetType();");
        for (int i = 0; i < classNames.Count; i++)
        {
            string className = classNames[i];
            string keyType = keyTypes[i];
            stringBuilder.AppendLine($"\t\t\tif (type == typeof(Dictionary<{keyType}, {className}>))");
            stringBuilder.AppendLine("\t\t\t{");
            stringBuilder.AppendLine($"\t\t\t\treturn ZeroFormatterSerializer.Serialize(data as Dictionary<{keyType}, {className}>);");
            stringBuilder.AppendLine("\t\t\t}");
            stringBuilder.AppendLine();
        }
        stringBuilder.AppendLine("\t\t\treturn null;");
        stringBuilder.AppendLine("\t\t}");

        stringBuilder.AppendLine();
        for (int i = 0; i < classNames.Count; i++)
        {
            string className = classNames[i];
            string keyType = keyTypes[i];
            stringBuilder.AppendLine($"\t\tpublic static void Deserialize(byte[] buffer, out Dictionary<{keyType}, {className}> datas)");
            stringBuilder.AppendLine("\t\t{");
            stringBuilder.AppendLine($"\t\t\tdatas = ZeroFormatterSerializer.Deserialize<Dictionary<{keyType}, {className}>>(buffer);");
            stringBuilder.AppendLine("\t\t}");
            stringBuilder.AppendLine();
        }

        stringBuilder.AppendLine("\t}");
        stringBuilder.AppendLine("}");

        StreamWriter writer = File.CreateText(Output_ZeromatterAdd);
        writer.Write(stringBuilder.ToString());
        writer.Flush();
        writer.Close();
    }

    private static void GenerateTableMgrScript(List<string> keyTypes, List<string> classNames)
    {
        StringBuilder stringBuilder = new StringBuilder();

        stringBuilder.AppendLine("using System.Collections.Generic;");
        stringBuilder.AppendLine("using ZeroFormatter;");
        stringBuilder.AppendLine("using System;");
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("namespace "+ NAMESPACE);
        stringBuilder.AppendLine("{");

        stringBuilder.AppendLine("\tpublic partial class TableMgr");
        stringBuilder.AppendLine("\t{");
        stringBuilder.AppendLine("\t\tprivate readonly Dictionary<string, Action<byte[]>> m_DataHandlers = new Dictionary<string, Action<byte[]>>();");
        stringBuilder.AppendLine("\t\tprivate static TableMgr instance = new TableMgr();");
        stringBuilder.AppendLine("\t\tpublic static TableMgr Instance { get { return instance; } }");
        for (int i = 0; i < classNames.Count; i++)
        {
            string className = classNames[i];
            string keyType = keyTypes[i];
            stringBuilder.AppendLine($"\t\tpublic Dictionary<{keyType}, {className}> {className}Dic;");
        }

        stringBuilder.AppendLine();
        stringBuilder.AppendLine("\t\tprivate TableMgr()");
        stringBuilder.AppendLine("\t\t{");
        for (int i = 0; i < classNames.Count; i++)
        {
            string className = classNames[i];
            string keyType = keyTypes[i];
            stringBuilder.AppendLine($"\t\t\tm_DataHandlers.Add(\"{className}.bytes\", (buffer) => {{ ZeroFormatterInitializer.Deserialize(buffer, out {className}Dic); }});");
        }
        stringBuilder.AppendLine("\t\t}");

        stringBuilder.AppendLine("\t}");
        stringBuilder.AppendLine("}");

        StreamWriter writer = File.CreateText(Output_CSharp + "/TableMgr.cs");
        writer.Write(stringBuilder.ToString());
        writer.Flush();
        writer.Close();
    }

    private static List<string> ReadComments(string line, List<int> invalidColumns)
    {
        List<string> comments = new List<string>();

        int currentColumn = 0;
        MatchCollection matchCollection = regex.Matches(line);
        foreach (var match in matchCollection)
        {
            string data = match.ToString();
            data = data.Replace("\n", " ").Replace("\r", " ");
            if (data.StartsWith("#", StringComparison.CurrentCulture))
            {
                invalidColumns.Add(currentColumn);
            }
            else
            {
                comments.Add(data);
            }
            currentColumn++;
        }
        return comments;
    }

    private static List<string> ReadFieldNames(string line, List<int> invalidColumns)
    {
        List<string> fieldNams = new List<string>();

        int currentColumn = 0;
        MatchCollection matchCollection = regex.Matches(line);
        foreach (var match in matchCollection)
        {
            string data = match.ToString();
            if (!invalidColumns.Contains(currentColumn))
            {
                fieldNams.Add(data);
            }
            currentColumn++;
        }
        return fieldNams;
    }

    private static List<string> ReadFieldTypes(string line, List<int> invalidColumns)
    {
        List<string> fieldTypes = new List<string>();

        int currentColumn = 0;
        MatchCollection matchCollection = regex.Matches(line);
        foreach (var match in matchCollection)
        {
            string data = match.ToString();
            if (!invalidColumns.Contains(currentColumn))
            {
                if (data.EndsWith(ARRAY_EXTENSION, StringComparison.CurrentCulture))
                {
                    data = data.Replace(ARRAY_EXTENSION, "[]");
                }
                fieldTypes.Add(data);
            }
            currentColumn++;
        }
        return fieldTypes;
    }

    private static List<string> ReadFieldValues(string line, List<int> invalidColumns)
    {
        List<string> fieldValues = new List<string>();

        int currentColumn = 0;
        MatchCollection matchCollection = regex.Matches(line);
        foreach (var match in matchCollection)
        {
            string data = match.ToString();
            if (!invalidColumns.Contains(currentColumn))
            {
                fieldValues.Add(data);
            }
            currentColumn++;
        }
        return fieldValues;
    }
   
    public static void GenerateZeroFomatter()
    {
        Process process = new Process();
        process.StartInfo.FileName = Path_ZeroFomatterTool;
        process.StartInfo.UseShellExecute = true;
        //process.StartInfo.Arguments = "para";
        process.Start();
        process.WaitForExit();
    }

    #region Export
    [MenuItem("TableTools/导出数据")]
    public static void ExprotBinary()
    {
        DirectoryInfo directoryInfo = new DirectoryInfo(Input_CSV);
        FileInfo[] csvFiles = directoryInfo.GetFiles("*", SearchOption.AllDirectories);
        foreach (FileInfo fileInfo in csvFiles)
        {
            if (fileInfo.FullName.EndsWith(".meta", true, CultureInfo.CurrentCulture))
            {
                continue;
            }

            ArrayList datas = ReadDatas(fileInfo);

            if (datas != null)
            {
                string className = NAMESPACE + "." + Path.GetFileNameWithoutExtension(fileInfo.Name);
                Type valueType = GetType(className);

                PropertyInfo keyProperty = valueType.GetProperties()[0];
                var dictionary = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(new Type[] { keyProperty.PropertyType, valueType }));
                var dictionaryAddMethod = dictionary.GetType().GetMethod("Add");
                foreach (var data in datas)
                {
                    dictionaryAddMethod.Invoke(dictionary, new object[] { keyProperty.GetValue(data), data });
                }

                string binaryPath = Output_Bytes +"/"+ Path.GetFileNameWithoutExtension(fileInfo.Name) + ".bytes.txt";
                SaveBinary(binaryPath, dictionary);
                //binaryPath = BINARY_OUTPUT_PATH_1 + Path.GetFileNameWithoutExtension(fileInfo.Name) + ".bytes.txt";
                //SaveBinary(binaryPath, dictionary);
            }
        }

        AssetDatabase.Refresh();
    }

    private static void SaveBinary(string filePath, object dataMap)
    {
        //只有unity运行才能获取到，editor下获取不到
        //Type type = Type.GetType("ZeroFormatter.ZeroFormatterInitializer");

        Assembly ass = GetAssembly("Assembly-CSharp");

        Type type = ass.GetType("ZeroFormatter.ZeroFormatterInitializer");

        type.InvokeMember("HandRegisterd",
System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Static
| System.Reflection.BindingFlags.Public, null, null,
null);

        using (FileStream fileStream = File.OpenWrite(filePath))
        {
            using (BinaryWriter binaryWriter = new BinaryWriter(fileStream, Encoding.Unicode))
            {

                object obj = type.InvokeMember("Serialize",
 System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Static
 | System.Reflection.BindingFlags.Public, null, null,
 new object[] { dataMap });

                byte[] buffer = obj as byte[];
                binaryWriter.Write(buffer);
                binaryWriter.Close();
            }
            fileStream.Close();
        }


        //ZeroFormatterInitializer.HandRegisterd();
        //using (FileStream fileStream = File.OpenWrite(filePath))
        //{
        //    using (BinaryWriter binaryWriter = new BinaryWriter(fileStream, Encoding.Unicode))
        //    {
        //        byte[] buffer = ZeroFormatterInitializer.Serialize(dataMap);
        //        binaryWriter.Write(buffer);
        //        binaryWriter.Close();
        //    }
        //    fileStream.Close();
        //}
    }

    private static ArrayList ReadDatas(FileInfo fileInfo)
    {
        string className = Path.GetFileNameWithoutExtension(fileInfo.Name);
        using (StreamReader streamReader = fileInfo.OpenText())
        {
            List<int> invalidColumns = new List<int>();
            List<string> comments = null;
            List<string> fieldNams = null;
            List<string> fieldTypes = null;

            string content = streamReader.ReadToEnd();
            string[] lines = content.Split(new string[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                return null;
            }

            ArrayList datas = new ArrayList();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                switch (i)
                {
                    case 0:
                        comments = ReadComments(line, invalidColumns);
                        break;

                    case 1:
                        fieldNams = ReadFieldNames(line, invalidColumns);
                        break;

                    case 2:
                        fieldTypes = ReadFieldTypes(line, invalidColumns);
                        break;

                    default:
                        List<string> fieldValues = ReadFieldValues(line, invalidColumns);
                        object data = CreateInstance(NAMESPACE + "." +className, fieldNams, fieldValues);
                        datas.Add(data);
                        break;
                }
            }
            return datas;
        }
    }

    private static object CreateInstance(string typeName, List<string> fieldNames, List<string> fieldValues)
    {
        Type type = GetType(typeName);

        if (null == type)
        {
            Debug.LogError("CreateInstance type==null :"+typeName);
        }

        object instance = Activator.CreateInstance(type);

        for (int i = 0; i < fieldNames.Count; i++)
        {
            PropertyInfo fieldInfo = type.GetProperty(fieldNames[i]);
            fieldInfo?.SetValue(instance, ReadValue(fieldInfo.PropertyType, fieldValues[i]));
        }

        return instance;
    }

    private static object ReadValue(Type fieldType, string value)
    {
        if (fieldType == typeof(int))
        {
            return ToInt(value);
        }

        if (fieldType == typeof(int[]))
        {
            return ToIntArray(value);
        }

        if (fieldType == typeof(long))
        {
            return ToLong(value);
        }

        if (fieldType == typeof(long[]))
        {
            return ToLongArray(value);
        }

        if (fieldType == typeof(float))
        {
            return ToFloat(value);
        }

        if (fieldType == typeof(float[]))
        {
            return ToFloatArry(value);
        }

        if (fieldType == typeof(double))
        {
            return ToDouble(value);
        }

        if (fieldType == typeof(double[]))
        {
            return ToDoubleArray(value);
        }

        if (fieldType == typeof(string))
        {
            return value;
        }

        if (fieldType == typeof(string[]))
        {
            return ToStringArray(value);
        }

        if (fieldType == typeof(char))
        {
            return ToChar(value);
        }

        if (fieldType == typeof(char[]))
        {
            return ToCharArray(value);
        }

        if (fieldType == typeof(byte))
        {
            return ToByte(value);
        }

        if (fieldType == typeof(byte[]))
        {
            return ToByteArray(value);
        }

        if (fieldType == typeof(bool))
        {
            return ToBoolean(value);
        }

        if (fieldType == typeof(bool[]))
        {
            return ToBooleanArray(value);
        }

        if (fieldType == typeof(object))
        {
            return value;
        }

        if (fieldType == typeof(object[]))
        {
            return ToObjectArray(value);
        }

        return null;
    }

    public static Type GetType(string typeName)
    {
        Type type = null;
        Assembly[] assemblyArray = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblyArray.Length; ++i)
        {
            type = assemblyArray[i].GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        for (int i = 0; i < assemblyArray.Length; ++i)
        {
            Type[] typeArray = assemblyArray[i].GetTypes();
            int typeArrayLength = typeArray.Length;
            for (int j = 0; j < typeArrayLength; ++j)
            {
                if (typeArray[j].Name.Equals(typeName))
                {
                    return typeArray[j];
                }
            }
        }
        return type;
    }
    #endregion


    #region Converter
    public static int ToInt(string value)
    {
        int result;
        if (int.TryParse(value, out result))
        {
            return result;
        }
        return 0;
    }

    public static byte ToByte(string value)
    {
        byte result;
        if (byte.TryParse(value, out result))
        {
            return result;
        }
        return 0;
    }

    public static float ToFloat(string value)
    {
        float result;
        if (float.TryParse(value, out result))
        {
            return result;
        }
        return 0;
    }

    public static double ToDouble(string value)
    {
        double result;
        if (double.TryParse(value, out result))
        {
            return result;
        }
        return 0;
    }

    public static long ToLong(string value)
    {
        long result;
        if (long.TryParse(value, out result))
        {
            return result;
        }
        return 0;
    }

    public static bool ToBoolean(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Equals("false", StringComparison.CurrentCultureIgnoreCase) || value.Equals("0"))
        {
            return false;
        }
        return true;
    }

    public static char ToChar(string value)
    {
        char result;
        if (char.TryParse(value, out result))
        {
            return result;
        }
        return default(char);
    }

    public static int[] ToIntArray(string value)
    {
        string[] values = ToStringArray(value);
        int[] array = new int[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToInt(values[i]);
        }
        return array;
    }

    public static float[] ToFloatArry(string value)
    {
        string[] values = ToStringArray(value);
        float[] array = new float[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToFloat(values[i]);
        }
        return array;
    }

    public static double[] ToDoubleArray(string value)
    {
        string[] values = ToStringArray(value);
        double[] array = new double[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToDouble(values[i]);
        }
        return array;
    }

    public static long[] ToLongArray(string value)
    {
        string[] values = ToStringArray(value);
        long[] array = new long[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToLong(values[i]);
        }
        return array;
    }

    public static bool[] ToBooleanArray(string value)
    {
        string[] values = ToStringArray(value);
        bool[] array = new bool[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToBoolean(values[i]);
        }
        return array;
    }

    public static char[] ToCharArray(string value)
    {
        string[] values = ToStringArray(value);
        char[] array = new char[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToChar(values[i]);
        }
        return array;
    }

    public static object[] ToObjectArray(string value)
    {
        string[] values = ToStringArray(value);
        object[] array = new object[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = values[i];
        }
        return array;
    }

    public static byte[] ToByteArray(string value)
    {
        string[] values = ToStringArray(value);
        byte[] array = new byte[values.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = ToByte(values[i]);
        }
        return array;
    }

    public static string[] ToStringArray(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new string[0];
        }

        value = value.Substring(1, value.Length - 2);
        return value.Split(new string[] { SEPARATOR }, StringSplitOptions.RemoveEmptyEntries);
    }
    #endregion

    #region tools
    /// <summary>
    /// unity editor下获取Assembly
    /// 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static System.Reflection.Assembly GetAssembly(string name)
    {
        var assArray = System.AppDomain.CurrentDomain.GetAssemblies();

        for (int i = 0; i < assArray.Length; i++)
        {
            var aname = assArray[i].GetName();

            if (aname.Name.Equals(name))
            {
                return assArray[i];
            }
        }

        return null;
    }

    public static System.Type[] GetAllDerivedTypes(System.AppDomain aAppDomain, System.Type aType)
    {
        var result = new List<System.Type>();
        var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes();
            foreach (var type in types)
            {
                if (type.IsSubclassOf(aType))
                    result.Add(type);
            }
        }
        return result.ToArray();
    }

    public static void ForceRebuild()
    {
        string[] rebuildSymbols = { "RebuildToggle1", "RebuildToggle2" };
        string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(
            EditorUserBuildSettings.selectedBuildTargetGroup);
        var definesStringTemp = definesString;
        if (definesStringTemp.Contains(rebuildSymbols[0]))
        {
            definesStringTemp = definesStringTemp.Replace(rebuildSymbols[0], rebuildSymbols[1]);
        }
        else if (definesStringTemp.Contains(rebuildSymbols[1]))
        {
            definesStringTemp = definesStringTemp.Replace(rebuildSymbols[1], rebuildSymbols[0]);
        }
        else
        {
            definesStringTemp += ";" + rebuildSymbols[0];
        }
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
            EditorUserBuildSettings.selectedBuildTargetGroup,
            definesStringTemp);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
            EditorUserBuildSettings.selectedBuildTargetGroup,
            definesString);
    }

    public static void DeleteFilesInFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        var filePaths = GetSubFilesPaths(folderPath);

        for (int i = 0; i < filePaths.Length; i++)
        {
            DeleteFileIfExists(filePaths[i]);
        }

    }

    public static string[] GetSubFilesPaths(string path)
    {
        return Directory.GetFiles(path);
    }

    public static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path)) return;      
        File.Delete(path);
    }

    public static string ChangeToAbsolutePath(string relativePath)
    {
        string s = Application.dataPath + relativePath.Substring("Assets".Length);
        return s;
    }

    #endregion

}
