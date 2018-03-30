//#define DEBUG_CMD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;


namespace LWJ.Build.Dependencies.UnityEditor
{

    public class FileDependency
    {
        public const string XmlNamespace = "urn:schema-lwj:build-dependencies";

        /// <summary>
        /// 项目目录下查找配置文件名
        /// </summary>
        public static string ConfigFileName = "dependencies.xml";

        /// <summary>
        /// 更新文件或目录间隔时间, update interval time seconds
        /// </summary>
        public static float UpdateInterval = 3;

        /// <summary>
        /// 开启日志消息, enable log message
        /// </summary>
        public static bool LogInfoEnable = true;

        private const string DependenciesNodeName = "dependencies";
        private const string IncludeNodeName = "include";
        private const string ItemNodeName = "item";
        private const string BeforeNodeName = "before";
        private const string AfterNodeName = "after";


        private static List<FileDependency> includeFiles;

        private string[] dirs;
        private IncludeItemInfo[] items;
        private string sourceFile;
        private bool isImport;
        public DateTime sourceFileLastChangedTime;
        private XmlNamespaceManager nsmgr;

        private DateTime nextUpdateTime;
        private double updateInterval;
        private bool logInfoEnable;
        private StringBuilder log;
        private List<FileDependency> children = new List<FileDependency>();

        static DateTime mainNextUpdateTime;


        public FileDependency(string sourceFile)
            : this(sourceFile, false)
        {

        }

        public FileDependency(string sourceFile, bool isImport)
        {

            this.sourceFile = sourceFile;
            this.isImport = isImport;
            log = new StringBuilder();
            this.nextUpdateTime = DateTime.Now;

            includeFiles.Add(this);
            if (File.Exists(sourceFile))
            {
                this.sourceFileLastChangedTime = File.GetLastWriteTimeUtc(sourceFile);
                var doc = new XmlDocument();
                doc.Load(sourceFile);
                nsmgr = new XmlNamespaceManager(doc.NameTable);

                var root = doc.DocumentElement;
                if (root != null && root.LocalName == DependenciesNodeName)
                {
                    if (root.NamespaceURI == "")
                        nsmgr.AddNamespace("f", "");
                    else
                        nsmgr.AddNamespace("f", XmlNamespace);

                    Load(root);
                }

            }
            else
            {
                if (isImport)
                    Debug.LogErrorFormat("import file not found, file:{0}", sourceFile);
            }
        }
        public bool IsSourceFileExists
        {
            get
            {
                if (sourceFile == null)
                    return false;
                return File.Exists(sourceFile);
            }
        }

        public bool IsSourceFileChanged
        {
            get
            {
                if (!string.IsNullOrEmpty(sourceFile))
                {
                    if (File.Exists(sourceFile))
                    {
                        DateTime dt = File.GetLastWriteTimeUtc(sourceFile);
                        return dt != sourceFileLastChangedTime;
                    }
                    else
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool IsImport
        {
            get
            {
                return isImport;
            }
        }

        public StringBuilder Log
        {
            get { return log; }
        }


        private void Load(XmlNode root)
        {
            List<string> dirList = null;
            XmlAttribute attr;

            logInfoEnable = false;
            updateInterval = UpdateInterval;


            attr = root.SelectSingleNode("@log", nsmgr) as XmlAttribute;
            if (attr != null)
            {
                bool b;
                if (bool.TryParse(attr.Value, out b))
                {
                    logInfoEnable = b;
                }
            }

            attr = root.SelectSingleNode("@interval", nsmgr) as XmlAttribute;
            if (attr != null)
            {
                float f;
                if (float.TryParse(attr.Value, out f))
                {
                    updateInterval = f;
                }
            }

            foreach (XmlNode node in root.SelectNodes("f:dir", nsmgr))
            {
                string path = (node.InnerText ?? string.Empty).Trim();

                path = path.Trim();
                if (path.Length == 0)
                    continue;
                if (dirList == null)
                    dirList = new List<string>();
                dirList.Add(path);
            }

            if (dirList != null)
                dirs = dirList.ToArray();

            List<IncludeItemInfo> items = new List<IncludeItemInfo>();

            foreach (XmlNode node in root.ChildNodes)
            {
                switch (node.LocalName)
                {
                    case IncludeNodeName:
                        ParseImportNode(node);
                        break;
                    case ItemNodeName:
                        ParseIncludeNode(null, node, items);
                        break;
                }

            }

            this.items = items.ToArray();
        }


        private void ParseImportNode(XmlNode node)
        {
            string path = (node.InnerText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(path))
                return;
            string fullPath = FindFile(path);
            if (fullPath != null)
            {
                if (ContainsIncludeFile(fullPath))
                    return;
            }
            FileDependency fd;
            if (fullPath != null)
                fd = new FileDependency(fullPath, true);
            else
                fd = new FileDependency(path, true);
            fd.children.Add(this);
        }

        private void ParseIncludeNode(IncludeItemInfo parent, XmlNode node, List<IncludeItemInfo> items)
        {
            string fromPath = GetAttributeValue(node, "from", string.Empty).Trim();
            string toPath = GetAttributeValue(node, "to", string.Empty).Trim();
            string[] extensionNames = GetAttributeValue(node, "ext", string.Empty).Trim().Split(',');

            if (extensionNames.Length == 0)
                extensionNames = new string[] { "" };

            foreach (var extName in extensionNames)
            {
                string fromPath2 = fromPath;
                if (extName.Length > 0)
                    fromPath2 += "." + extName;
                IncludeItemInfo item = new IncludeItemInfo(fromPath2, toPath, parent);
                item.ExtName = extName;
                foreach (XmlNode childNode in node.ChildNodes)
                {
                    switch (childNode.LocalName)
                    {
                        case ItemNodeName:
                            ParseIncludeNode(item, childNode, items);
                            break;
                        case BeforeNodeName:
                            if (IsExtNameMatch(extName, childNode))
                            {
                                ParseBeforeNode(item, childNode);
                            }
                            break;
                        case AfterNodeName:
                            if (IsExtNameMatch(extName, childNode))
                            {
                                ParseAfterNode(item, childNode);
                            }
                            break;
                    }

                }

                if (item.Children == null || item.Children.Count == 0)
                    items.Add(item);
                if (parent != null)
                    parent.AddChild(item);
            }


        }

        private bool _IsLogEnabled
        {
            get
            {
                if (LogInfoEnable || logInfoEnable)
                    return true;
                foreach (var item in children)
                {
                    if (item._IsLogEnabled)
                        return true;
                }
                return false;
            }
        }

        private bool IsExtNameMatch(string extName, XmlNode node)
        {
            var extAttr = GetAttributeValue(node, "ext", null);
            if (string.IsNullOrEmpty(extAttr))
                return true;
            extAttr = extAttr.Trim();
            if (extAttr.Length == 0)
                return true;
            foreach (string part in extAttr.Split(','))
            {
                if (part.Length == 0 || string.Equals(part, extName, StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }
            return false;
        }

        private void ParseBeforeNode(IncludeItemInfo item, XmlNode node)
        {
            string cmdText = (node.InnerText ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(cmdText))
                return;

            if (item.Before == null)
                item.Before = cmdText;
            else
                item.Before += Environment.NewLine + cmdText;
        }
        private void ParseAfterNode(IncludeItemInfo item, XmlNode node)
        {
            string cmdText = (node.InnerText ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(cmdText))
                return;
            if (item.After == null)
                item.After = cmdText;
            else
                item.After += Environment.NewLine + cmdText;
        }


        private string GetAttributeValue(XmlNode node, string attrName, string defaultValue)
        {
            XmlAttribute attr;
            attr = node.Attributes[attrName];
            if (attr == null)
                return defaultValue;
            return attr.Value;
        }

        public bool Refresh()
        {
            bool changed = false;

            if (items != null)
            {
                Log.Length = 0;

                foreach (var item in items)
                {
                    changed |= Refresh(item);
                }

                if (Log.Length > 0)
                {
                    if (_IsLogEnabled)
                        Debug.Log(Log.ToString());
                    Log.Length = 0;
                }
            }
            return changed;
        }

        private bool Refresh(IncludeItemInfo item)
        {

            if (string.IsNullOrEmpty(item.FullFromPath) || string.IsNullOrEmpty(item.FullToPath))
                return false;


            bool isCompleted = false;
            bool changed = false;

            string fullFromPath = null, fullToPath = null;


            foreach (var fromPath in EnumerateFindPaths(item))
            {
                fullFromPath = Path.GetFullPath(fromPath);
                if (File.Exists(fullFromPath))
                {
                    fullToPath = Path.Combine(item.FullToPath, Path.GetFileName(fullFromPath));
                    if (CanUpdateFile(fullFromPath, fullToPath))
                    {
                        if (!string.IsNullOrEmpty(item.Before))
                        { 
                            ExecuteCommand(Path.GetDirectoryName(fullFromPath), item.Before);
                        }
                        changed |= UpdateFile(item, fullFromPath, fullToPath);

                        if (!string.IsNullOrEmpty(item.After))
                        { 
                            ExecuteCommand(Path.GetDirectoryName(fullFromPath), item.After);
                        }
                    }
                    else
                    { 
                    }

                    isCompleted = true;
                    break;
                }
                else if (Directory.Exists(fullFromPath))
                {
                    bool isUpdate = false;
                    foreach (var file in Directory.GetFiles(fullFromPath, "*", SearchOption.AllDirectories))
                    {
                        string fromRelativePath = file.Substring(fullFromPath.Length);
                        fullToPath = Path.Combine(item.FullToPath, fromRelativePath);
                        if (CanUpdateFile(file, fullToPath))
                        {
                            if (!isUpdate)
                            {
                                if (!string.IsNullOrEmpty(item.Before))
                                { 
                                    ExecuteCommand(fullFromPath, item.Before);
                                }
                            }
                            changed |= UpdateFile(item, file, fullToPath);
                            isUpdate = true;
                        }
                    }

                    if (isUpdate)
                    {
                        if (!string.IsNullOrEmpty(item.After))
                        { 
                            ExecuteCommand(fullFromPath, item.After);
                        }
                    }

                    isCompleted = true;
                    break;
                }
            }


            if (!isCompleted)
            {
                Debug.LogErrorFormat("include file not found.\r\nfile: {0}", item.FullFromPath);
            }
            return changed;
        }

        private void ExecuteCommand(string baseDir, string cmdText)
        {
            if (string.IsNullOrEmpty(cmdText))
                return;
            Debug.LogFormat("execute command\r\n{0}\r\n{1}", cmdText, baseDir);

            ProcessStartInfo startInfo = new ProcessStartInfo("cmd");
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.WorkingDirectory = baseDir;
     
#if DEBUG_CMD
            
            startInfo.CreateNoWindow = false;
            startInfo.WindowStyle = ProcessWindowStyle.Normal;
            startInfo.RedirectStandardError = false;
#else

            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardErrorEncoding = Encoding.Default;
#endif
           
            using (var process = Process.Start(startInfo))
            {
                Encoding encoding = process.StandardInput.Encoding;

                var input = process.StandardInput;
          
                input.WriteLine(); 
                input.WriteLine(cmdText); 
                input.Flush();  

                input.WriteLine("exit");
                input.Flush(); 
                
#if !DEBUG_CMD
                using (MemoryStream ms = new MemoryStream())
                using (var reader = new StreamReader(ms, Encoding.UTF8))
                {
              
                    process.WaitForExit();
            
                    string error = process.StandardError.ReadToEnd();  
                    if (!string.IsNullOrEmpty(error))
                    {
                       
                        //throw new Exception(error);
                        Debug.LogError(error); 
                        //错误:句柄无效
                    }
                  
                }
              
#endif
            }

        }

        string FindFile(string path)
        {
            foreach (var path2 in EnumerateFindPaths(path))
            {
                if (File.Exists(path2))
                    return path2;
            }
            return null;
        }
        IEnumerable<string> EnumerateFindPaths(IncludeItemInfo item)
        {
            return EnumerateFindPaths(item.FullFromPath);
        }
        IEnumerable<string> EnumerateFindPaths(string path)
        {

            IEnumerable<string> list = new string[] { "" };

            if (dirs != null && dirs.Length > 0)
                list = list.Concat(dirs);

            foreach (var dir in list)
            {
                if (Path.IsPathRooted(path))
                {
                    yield return Path.Combine(path, dir);
                }
                else
                {
                    DirectoryInfo dirInfo;
                    if (isImport)
                    {
                        dirInfo = new DirectoryInfo(Path.GetDirectoryName(sourceFile));
                    }
                    else
                    {
                        dirInfo = Directory.GetParent(".");
                    }
                    while (dirInfo.Parent != null)
                    {
                        yield return Path.Combine(dirInfo.FullName, Path.Combine(dir, path));
                        dirInfo = dirInfo.Parent;
                    }

                    yield return Path.Combine(dirInfo.Root.FullName, Path.Combine(dir, path));
                }
            }

        }

        public string ResolveFromFileFullPath(string path)
        {
            string fullPath;
            if (Path.IsPathRooted(path))
            {
                fullPath = path;
            }
            else
            {
                if (isImport)
                {
                    fullPath = Path.GetDirectoryName(sourceFile);
                    fullPath = Path.Combine(fullPath, path);
                }
                else
                {
                    fullPath = Path.GetFullPath(path);
                }
            }
            return fullPath;
        }


        bool CanUpdateFile(string fromFile, string toFile)
        {
            DateTime fromFileLastWriteTime = File.GetLastWriteTimeUtc(fromFile);
            DateTime toFileLastWriteTime;
            if (!File.Exists(toFile))
                return true;
            toFileLastWriteTime = File.GetLastWriteTimeUtc(toFile);
            if (fromFileLastWriteTime != toFileLastWriteTime)
            {
                return true;
            }
            return false;
        }

        bool UpdateFile(IncludeItemInfo item, string fromFile, string toFile)
        {

            if (CanUpdateFile(fromFile, toFile))
            {

                string dir = Path.GetDirectoryName(toFile);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(fromFile, toFile, true);

                if (LogInfoEnable)
                    Log.AppendFormat("include file ok. [{0}] > [{1}]\r\n", fromFile, toFile);
                return true;
            }
            return false;
        }

        private static bool ContainsIncludeFile(string includeFile)
        {
            if (includeFile == null)
                throw new ArgumentNullException("includeFile");
            if (includeFiles == null)
                return false;
            var includeFileInfo = new FileInfo(includeFile);
            if (!includeFileInfo.Exists)
                return false;
            foreach (var file in includeFiles)
            {
                var f = new FileInfo(file.sourceFile);
                if (f.Exists)
                {
                    if (f.FullName == includeFileInfo.FullName)
                        return true;
                }
            }
            return false;
        }



        [InitializeOnLoadMethod]
        public static void OnInit()
        {

            includeFiles = new List<FileDependency>();
            string configFilename = ConfigFileName;
            mainNextUpdateTime = DateTime.Now;
            foreach (var findPath in new string[] { "ProjectSettings", "Assets" })
            {
                foreach (var path in Directory.GetFiles(Path.GetFullPath(findPath), configFilename, SearchOption.AllDirectories))
                {
                    new FileDependency(path);
                }
            }

            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;

        }

        private static void OnUpdate()
        {

            if (EditorApplication.isPlaying || EditorApplication.isCompiling)
                return;

            DateTime now = DateTime.Now;

            if (DateTime.Now < mainNextUpdateTime)
                return;

            mainNextUpdateTime = now.AddSeconds(1f);

            bool changed = false;

            if (includeFiles != null)
            {
                changed = false;
                foreach (var item in includeFiles)
                {
                    changed |= item.IsSourceFileChanged;
                    if (changed)
                    {
                        if (item._IsLogEnabled)
                            Debug.LogFormat("reload all config.\r\nfile changed: {0}", item.sourceFile);
                        break;
                    }
                }
                if (changed)
                {
                    OnInit();
                    return;
                }
            }

            changed = false;
            foreach (var item in includeFiles)
            {
                if (now > item.nextUpdateTime)
                {
                    item.nextUpdateTime = DateTime.Now.AddSeconds(item.updateInterval);
                    changed |= item.Refresh();
                }
            }
            if (changed)
            {
                AssetDatabase.Refresh(ImportAssetOptions.Default);
            }


        }




        class IncludeItemInfo
        {
            private string fromPath;
            private string toPath;
            private IncludeItemInfo parent;
            private List<IncludeItemInfo> children;
            private string fullFromPath;
            private string fullToPath;
            private string fileName;

            public IncludeItemInfo(string fromPath, string toPath, IncludeItemInfo parent)
            {
                this.fromPath = fromPath;
                this.toPath = toPath;
                this.fullFromPath = fromPath;
                this.fullToPath = toPath;

                if (!string.IsNullOrEmpty(fromPath))
                    fileName = Path.GetFileName(fromPath);
                else
                    fileName = null;

                if (parent != null)
                {
                    fullFromPath = CombinePath(parent.FullFromPath, fullFromPath);
                    fullToPath = CombinePath(parent.FullToPath, fullToPath);
                }

            }

            public string FromPath
            {
                get
                {
                    return fromPath;
                }
            }

            public string ToPath
            {
                get
                {
                    return toPath;
                }
            }

            public string FullFromPath
            {
                get
                {
                    return fullFromPath;
                }
            }
            public string FullToPath
            {
                get
                {
                    return fullToPath;
                }
            }

            public string ExtName { get; set; }
            public string Before { get; set; }
            public string After { get; set; }


            public IncludeItemInfo Parent
            {
                get
                {
                    return parent;
                }
            }

            public List<IncludeItemInfo> Children
            {
                get
                {
                    return children;
                }
            }


            public void AddChild(IncludeItemInfo child)
            {
                if (children == null)
                    children = new List<IncludeItemInfo>();

                child.parent = this;
                children.Add(child);
            }

        }

        static string CombinePath(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1))
                return path2;
            if (string.IsNullOrEmpty(path2))
                return path1;
            return Path.Combine(path1, path2);
        }

        public override string ToString()
        {
            return sourceFile;
        }

    }
}
