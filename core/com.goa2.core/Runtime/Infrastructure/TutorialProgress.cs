#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Goa2.Infrastructure
{
    public sealed class TutorialProgress
    {
        public int Version=TutorialCourse.Version;
        public int ResumeChapter;
        public bool[] Completed=new bool[TutorialCourse.Chapters.Length];
        public bool Hints=true;
        public string[] SeenHints=Array.Empty<string>();
        public static TutorialProgress Read(string path,out string message)
        {
            message="";if(!File.Exists(path))return new TutorialProgress();
            try
            {
                var data=JsonConvert.DeserializeObject<TutorialProgress>(File.ReadAllText(path),new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MaxDepth=8});
                if(data==null || data.Version!=TutorialCourse.Version || data.Completed==null || data.SeenHints==null || data.Completed.Length!=TutorialCourse.Chapters.Length || data.ResumeChapter<0 || data.ResumeChapter>=TutorialCourse.Chapters.Length)
                    throw new InvalidDataException("教程版本或进度格式已变化");
                return data;
            }
            catch(Exception e) when(e is IOException || e is JsonException || e is InvalidDataException)
            {message="旧教程进度无法读取，将从章节起点重练；真实对局不受影响。";return new TutorialProgress();}
        }
        public void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(this,Formatting.Indented));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
    }
}
