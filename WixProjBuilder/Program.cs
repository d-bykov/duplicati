﻿using System;
using System.Collections.Generic;
using System.Text;

namespace WixProjBuilder
{
    class Program
    {
        static void Main(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = Duplicati.CommandLine.CommandLineParser.ExtractOptions(args);

            if (args.Count != 1)
            {
                Console.WriteLine("Usage: ");
                Console.WriteLine("  WixProjBuilder.exe <projfile> [option=value]");
                return;
            }

            if (!System.IO.File.Exists(args[0]))
            {
                Console.WriteLine(string.Format("File not found: {0}", args[0]));
                return;
            }

            string wixpath;
            if (options.ContainsKey("wixpath"))
                wixpath = options["wixpath"];
            else
                wixpath = System.Environment.GetEnvironmentVariable("WIXPATH");

            if (string.IsNullOrEmpty(wixpath))
            {
                wixpath = System.IO.Path.Combine(System.IO.Path.Combine(System.Environment.ExpandEnvironmentVariables("%programfiles%"), "Windows Installer XML v3"), "bin");
                Console.WriteLine(string.Format("*** wixpath not specified, using: {0}", wixpath));
            }

            args[0] = System.IO.Path.GetFullPath(args[0]);

            Console.WriteLine(string.Format("Parsing wixproj file: {0}", args[0]));

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(args[0]);

            string config = "Release";
            if (options.ContainsKey("configuration"))
                config = options["configuration"];

            string projdir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0]));

            List<string> includes = new List<string>();
            List<string> refs = new List<string>();
            List<string> content = new List<string>();

            System.Xml.XmlNamespaceManager nm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nm.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003");

            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:ItemGroup/ms:Compile", nm))
                includes.Add(n.Attributes["Include"].Value);

            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:ItemGroup/ms:Content", nm))
                content.Add(n.Attributes["Include"].Value);

            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:ItemGroup/ms:WixExtension", nm))
                refs.Add(n.Attributes["Include"].Value);


            string objdir = System.IO.Path.Combine(System.IO.Path.Combine(projdir, "obj"), config);
            string packagename = "output";
            string outdir = System.IO.Path.Combine("bin", config);
            string outtype = "Package";

            //TODO: Support multiconfiguration system correctly
            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:PropertyGroup/ms:Configuration", nm))
                if (true) //if (string.Compare(n.InnerText, config, true) == 0)
                {
                    System.Xml.XmlNode p = n.ParentNode;
                    if (p["OutputName"] != null)
                        packagename = p["OutputName"].InnerText;
                    if (p["OutputType"] != null)
                        outtype = p["OutputType"].InnerText;
                    if (p["OutputPath"] != null)
                        outdir = p["OutputPath"].InnerText.Replace("$(Configuration)", config);
                    if (p["IntermediateOutputPath"] != null)
                        objdir = p["IntermediateOutputPath"].InnerText.Replace("$(Configuration)", config);
                }

            if (!objdir.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                objdir += System.IO.Path.DirectorySeparatorChar;

            string msiname = System.IO.Path.Combine(outdir, packagename + ".msi");

            Console.WriteLine("  Compiling ... ");
            if (includes.Count == 0)
            {
                Console.WriteLine("No files found to compile in project file");
                return;
            }

            string compile_args = "\"" + string.Join("\" \"", includes.ToArray()) + "\"";
            compile_args += " -out \"" + objdir.Replace("\\", "\\\\") + "\"";

            int res = Execute(
                System.IO.Path.Combine(wixpath, "candle.exe"), 
                projdir,
                compile_args);

            if (res != 0)
            {
                Console.WriteLine("Compilation failed, aborting");
                return;
            }

            Console.WriteLine("  Linking ...");

            for (int i = 0; i < includes.Count; i++)
                includes[i] = System.IO.Path.Combine(objdir, System.IO.Path.GetFileNameWithoutExtension(includes[i]) + ".wixobj");

            for (int i = 0; i < refs.Count; i++)
                if (!System.IO.Path.IsPathRooted(refs[i]))
                {
                    refs[i] = FindDll(refs[i] + ".dll", new string[] { projdir, wixpath });
                    if (!System.IO.Path.IsPathRooted(refs[i]))
                        refs[i] = FindDll(refs[i]);
                }

            string link_args = "\"" + string.Join("\" \"", includes.ToArray()) + "\"";

            if (refs.Count > 0)
                link_args += " -ext \"" + string.Join("\" -ext \"", refs.ToArray()) + "\"";

            link_args += " -out \"" + msiname + "\"";

            res = Execute(
                System.IO.Path.Combine(wixpath, "light.exe"),
                projdir,
                link_args);

            if (res != 0)
            {
                Console.WriteLine("Link failed, aborting");
                return;
            }

            if (!System.IO.Path.IsPathRooted(msiname))
                msiname = System.IO.Path.GetFullPath(System.IO.Path.Combine(projdir, msiname));

            Console.WriteLine(string.Format("Done: {0}", msiname));
        }

        private static int Execute(string app, string workdir, string args)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(app, args);
            psi.CreateNoWindow = true;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = workdir;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
            p.WaitForExit(60000); //Wait up to one minute
            if (!p.HasExited)
            {
                try { p.Kill(); }
                catch { }

                Console.WriteLine("Stdout: " + p.StandardOutput.ReadToEnd());
                Console.WriteLine("Stderr: " + p.StandardError.ReadToEnd());

                throw new Exception(string.Format("Application {0} hung", app));
            }

            Console.WriteLine();
            Console.WriteLine(p.StandardOutput.ReadToEnd());
            Console.WriteLine(p.StandardError.ReadToEnd());
            Console.WriteLine();


            return p.ExitCode;
        }

        private static string FindDll(string filename)
        {
            return FindDll(filename, System.Environment.GetEnvironmentVariable("path").Split(System.IO.Path.PathSeparator));
        }

        private static string FindDll(string filename, IEnumerable<string> paths)
        {
            foreach (string p in paths)
                if (System.IO.File.Exists(System.IO.Path.Combine(p, filename)))
                    return System.IO.Path.Combine(p, filename);

            return filename;
        }
    }
}
