//-----------------------------------------------------------------------
// <copyright file="GendarmeRunner.cs">
//   APL 2.0
// </copyright>
// <license>
//   Copyright 2015 Anas EL HAJJAJI
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
// </license>
//-----------------------------------------------------------------------
using Gendarme.Framework;
using Gendarme.Framework.Engines;

namespace MonoDevelop.Gendarme
{
    using System;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Gendarme runner.
    /// </summary>
    [EngineDependency(typeof(SuppressMessageEngine))]
    public class GendarmeRunner : Runner
    {
        /// <summary>
        /// The rule type filter.
        /// </summary>
        private static TypeFilter ruleTypeFilter = new TypeFilter(RuleFilter);

        /// <summary>
        /// Initializes a new instance of the <see cref="MonoDevelop.Gendarme.GendarmeRunner"/> class.
        /// </summary>
        public GendarmeRunner()
        {
            this.IgnoreList = new BasicIgnoreList(this);
        }

        /// <summary>
        /// Loads the rules.
        /// </summary>
        public void LoadRules()
        {
            // load every dll to check for rules...
            string dir = Path.GetDirectoryName(typeof(IRule).Assembly.Location);
            FileInfo[] files = new DirectoryInfo(dir).GetFiles("*.dll");
            foreach (FileInfo info in files)
            {
                // except for a few, well known, ones
                switch (info.Name)
                {
                    case "Mono.Cecil.dll":
                    case "Mono.Cecil.Pdb.dll":
                    case "Mono.Cecil.Mdb.dll":
                    case "Gendarme.Framework.dll":
                        continue;
                }

                this.LoadRulesFromAssembly(info.FullName);
            }
        }

        /// <summary>
        /// Execute the analysis.
        /// </summary>
        public void Execute()
        {
            try
            {
                this.Initialize();
                this.Run();
                this.TearDown();
            }
            catch
            {
                // Something wrong happened.
                // TODO Log or do something if happened.
            }
        }

        /// <summary>
        /// Build RuleFilter.
        /// </summary>
        /// <returns><c>true</c>, if filter was ruled, <c>false</c> otherwise.</returns>
        /// <param name="type">Type.</param>
        /// <param name="interfaceName">Interface name.</param>
        private static bool RuleFilter(Type type, object interfaceName)
        {
            return type.ToString() == (interfaceName as string);
        }

        /// <summary>
        /// Loads the rules from assembly.
        /// </summary>
        /// <param name="assemblyName">Assembly name.</param>
        private void LoadRulesFromAssembly(string assemblyName)
        {
            AssemblyName aname = AssemblyName.GetAssemblyName(Path.GetFullPath(assemblyName));
            Assembly a = Assembly.Load(aname);
            foreach (Type t in a.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface)
                {
                    continue;
                }

                if (t.FindInterfaces(ruleTypeFilter, "Gendarme.Framework.IRule").Length > 0)
                {
                    Rules.Add((IRule)Activator.CreateInstance(t));
                }
            }
        }
    }
}
