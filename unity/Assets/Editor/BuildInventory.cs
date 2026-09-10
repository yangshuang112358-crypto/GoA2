#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Goa2.Infrastructure;
using UnityEngine;

namespace Goa2.Editor
{
    [Serializable]
    public sealed class BuildFileRecord
    {
        public string Path="",Sha256="";
        public long Bytes;
    }
    [Serializable]
    public sealed class PlayerBuildInfo
    {
        public int SchemaVersion=1,EngineVersion;
        public string UnityVersion="",ContentHash="",ProtocolVersion="",RulesVersion="",BuiltUtc="";
        public List<string> PrimaryCards=new List<string>(),DefenseCards=new List<string>();
        public List<BuildFileRecord> Files=new List<BuildFileRecord>(),SourceFiles=new List<BuildFileRecord>();
    }
    internal static class BuildInventory
    {
        // Keep these input roots aligned with tools/player_package.py. Generated catalog
        // copies are excluded here and covered by the original content and Player payload.
        private static readonly string[] SourceDirectories={"core/com.goa2.core/Runtime","unity/Assets","unity/Packages","unity/ProjectSettings","content/canonical","tests/scenarios"};
        private static string Relative(string root,string file) => file.Substring(root.TrimEnd(System.IO.Path.DirectorySeparatorChar).Length+1).Replace('\\','/');
        private static BuildFileRecord Record(string root,string path)
        {
            string relative=Relative(root,path);
            for(var info=new FileInfo(path) as FileSystemInfo; info!=null && info.FullName!=root; info=info is FileInfo file ? file.Directory : ((DirectoryInfo)info).Parent)
                if((info.Attributes & FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException("Linked build input: "+relative);
            using var stream=File.OpenRead(path); using var hash=SHA256.Create();
            return new BuildFileRecord { Path=relative,Bytes=stream.Length,Sha256=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant() };
        }
        internal static List<BuildFileRecord> CaptureSources(string root)
        {
            var files=SourceDirectories.SelectMany(directory=>Directory.EnumerateFiles(System.IO.Path.Combine(root,directory),"*",SearchOption.AllDirectories))
                .Append(System.IO.Path.Combine(root,"content","manifest.json"))
                .Append(System.IO.Path.Combine(root,"core","com.goa2.core","package.json"))
                .Append(System.IO.Path.Combine(root,"tools","debug-positions.json"))
                .Where(file=>Relative(root,file)!="unity/Assets/StreamingAssets/Goa2.meta" && !Relative(root,file).StartsWith("unity/Assets/StreamingAssets/Goa2/",StringComparison.Ordinal))
                .Where(file=>Relative(root,file)!="unity/Assets/StreamingAssets/Goa2Debug.meta" && !Relative(root,file).StartsWith("unity/Assets/StreamingAssets/Goa2Debug/",StringComparison.Ordinal))
                .OrderBy(file=>Relative(root,file),StringComparer.Ordinal);
            return files.Select(file=>Record(root,file)).ToList();
        }
        internal static void Write(string root,string player,List<BuildFileRecord> captured)
        {
            var after=CaptureSources(root);
            string Key(BuildFileRecord file) => file.Path+":"+file.Bytes+":"+file.Sha256;
            if(!captured.Select(Key).SequenceEqual(after.Select(Key)))
                throw new InvalidOperationException("Build inputs changed during the build. Rebuild after edits finish.");
            var catalog=ContentLoader.LoadDirectory(root);
            var capabilities=LocalGameFactory.Create(catalog,"build-capabilities",new[] {"A","B","C","D"},42).View(null);
            var info=new PlayerBuildInfo
            {
                EngineVersion=GameState.CurrentEngineVersion,UnityVersion=UnityEngine.Application.unityVersion,ContentHash=catalog.Hash,
                ProtocolVersion=GameState.CurrentProtocol,RulesVersion=catalog.Rules.Version,BuiltUtc=DateTime.UtcNow.ToString("o"),
                PrimaryCards=capabilities.SupportedPrimaryCards,DefenseCards=capabilities.SupportedDefenseCards,SourceFiles=after,
                Files=Directory.EnumerateFiles(player,"*",SearchOption.AllDirectories)
                    .Where(file=>Relative(player,file)!="build-info.json")
                    .OrderBy(file=>Relative(player,file),StringComparer.Ordinal).Select(file=>Record(player,file)).ToList()
            };
            File.WriteAllText(System.IO.Path.Combine(player,"build-info.json"),JsonUtility.ToJson(info,true),new UTF8Encoding(false));
        }
    }
}
