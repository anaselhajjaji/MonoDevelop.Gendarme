using System;
using System.Reflection;
using System.IO;

using Gendarme.Framework;
using Gendarme.Framework.Engines;

namespace MonoDevelop.Gendarme
{
    [EngineDependency (typeof (SuppressMessageEngine))]
    public class GendarmeRunner : Runner
    {
        private static TypeFilter RuleTypeFilter = new TypeFilter (RuleFilter);

        public GendarmeRunner ()
        {
            IgnoreList = new BasicIgnoreList (this);
        }

        private static bool RuleFilter (Type type, object interfaceName)
        {
            return (type.ToString () == (interfaceName as string));
        }

        private void LoadRulesFromAssembly (string assemblyName)
        {
            AssemblyName aname = AssemblyName.GetAssemblyName (Path.GetFullPath (assemblyName));
            Assembly a = Assembly.Load (aname);
            foreach (Type t in a.GetTypes ()) {
                if (t.IsAbstract || t.IsInterface)
                    continue;
                if (t.FindInterfaces (RuleTypeFilter, "Gendarme.Framework.IRule").Length > 0) {
                    Rules.Add ((IRule) Activator.CreateInstance (t));
                }
            }
        }

        public void LoadRules ()
        {
            // load every dll to check for rules...
            string dir = Path.GetDirectoryName (typeof (IRule).Assembly.Location);
            FileInfo [] files = new DirectoryInfo (dir).GetFiles ("*.dll");
            foreach (FileInfo info in files) {
                // except for a few, well known, ones
                switch (info.Name) {
                case "Mono.Cecil.dll":
                case "Mono.Cecil.Pdb.dll":
                case "Mono.Cecil.Mdb.dll":
                case "Gendarme.Framework.dll":
                    continue;
                }
                LoadRulesFromAssembly (info.FullName);
            }
        }

        public void Execute ()
        {
            try {
                Initialize ();
                Run ();
                TearDown ();
            }
            catch (Exception e) {
            }
        }
    }
}

