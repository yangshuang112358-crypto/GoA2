#nullable enable
using System;
using System.Linq;
using System.IO;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class TutorialTests
    {
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)]
        [TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)][TestCase(9)]
        public void CompleteChapterWithAcceptedCommandsAndReplay(int chapter)
        {
            var catalog=BattlefieldTests.Catalog();var c=new TutorialCourse(catalog,chapter);
            var initial=c.Session.View(0);Assert.That(initial.QuickSelection,Is.False);
            Assert.That(initial.Players[0].HeroId,Is.EqualTo("sabina"));
            for(int guard=0;!c.Complete && guard<60;guard++)
            {
                Assert.That(c.Error,Is.Empty);var id=c.Line.Id;var v=c.Session.View(0);
                if(c.Line.Reading){Assert.That(c.Acknowledge(),Is.True);continue;}
                if(c.TickOpponent())continue;
                Assert.That(c.Error,Is.Empty);
                if(id=="read-card" || id=="move-preview" || id=="move-cancel"){Assert.That(c.Signal(id),Is.True);continue;}
                CommandKind kind;string value="";Hex dest=default;
                switch(id)
                {
                    case "move-commit":case "practice":kind=CommandKind.Move;dest=v.SecondaryMoves.First(m=>m.Destination!=v.Units.Single(u=>u.Seat==0).Position).Destination;break;
                    case "select-first":kind=CommandKind.SelectCard;value="sabina-00-近身射击";break;
                    case "select-change":case "select-final":kind=CommandKind.SelectCard;value="sabina-01-拔枪";break;
                    case "select-cancel":kind=CommandKind.CancelCardSelection;break;
                    case "select-confirm":kind=CommandKind.ConfirmCard;break;
                    case "attack-hero":kind=CommandKind.CommitPrimaryAttack;value="hero:1";break;
                    case "defend":kind=CommandKind.Defend;value="sabina-01-拔枪";break;
                    case "attack-minion":case "attack-heavy":kind=CommandKind.CommitPrimaryAttack;value=c.TargetId;break;
                    case "round-start":case "upgrade-start":case "ultimate-start":kind=CommandKind.ResolveRoundEnd;break;
                    case "round-remove":kind=CommandKind.ChooseRoundMinionRemoval;value=v.RoundMinionRemovals.First();break;
                    case "upgrade-pick":case "ultimate-pick":kind=CommandKind.ChooseUpgrade;value=v.UpgradeOptions.First().CardId;break;
                    default:throw new Exception("Unhandled "+id);
                }
                var result=c.Execute(kind,value,destination:dest);Assert.That(result.Accepted,Is.True,id+": "+result.Code+" "+result.Message);
            }
            Assert.That(c.Complete,Is.True,c.Line.Id);var end=c.Session.View(0);
            if(chapter==2)Assert.That(end.RedCrystal,Is.EqualTo(initial.RedCrystal-1));
            if(chapter==3){Assert.That(end.BlueCrystal,Is.EqualTo(initial.BlueCrystal));Assert.That(end.OwnCards.Single(x=>x.CardId=="sabina-01-拔枪").Zone,Is.EqualTo(CardZone.Discarded));}
            if(chapter==4)Assert.That(end.BlueMarks,Is.Zero);
            if(chapter==5)Assert.That(end.BlueMarks,Is.EqualTo(1));
            if(chapter==6){Assert.That(end.RedMarks,Is.EqualTo(1));Assert.That(end.Players[0].Gold,Is.LessThanOrEqualTo(1));}
            if(chapter==7)Assert.That(end.Players[0].Level,Is.EqualTo(2));
            if(chapter==8){Assert.That(end.Players[0].Level,Is.EqualTo(8));Assert.That(end.Players[0].PurpleCardId,Is.EqualTo("sabina-12-重型枪械"));Assert.That(end.OwnCards.Count,Is.EqualTo(5));}
            Assert.That(LocalGameFactory.Restore(catalog,c.Session.ExportSave()).ExportSave(),Is.EqualTo(c.Session.ExportSave()));
        }
        [Test]
        public void WrongCommandsNeverAdvanceOrMutateAndRetryCreatesSeparateMatch()
        {
            var c=new TutorialCourse(BattlefieldTests.Catalog());string before=c.Session.ExportSave();
            Assert.That(c.Execute(CommandKind.DebugGold,"999",0).Accepted,Is.False);
            Assert.That(c.Execute(CommandKind.Move,destination:new Hex(999,999)).Accepted,Is.False);
            Assert.That(c.Signal("move-preview"),Is.False);Assert.That(c.Step,Is.Zero);
            Assert.That(c.Session.ExportSave(),Is.EqualTo(before));
            c.Acknowledge();c.Signal("read-card");c.Signal("move-preview");c.Signal("move-cancel");
            before=c.Session.ExportSave();Assert.That(c.Execute(CommandKind.Move,destination:new Hex(999,999)).Accepted,Is.False);
            Assert.That(c.Session.ExportSave(),Is.EqualTo(before));Assert.That(c.Line.Id,Is.EqualTo("move-commit"));
            var match=c.Session.View(0).MatchId;c.Start(0);Assert.That(c.Step,Is.Zero);Assert.That(c.Session.View(0).MatchId,Is.Not.EqualTo(match));
        }
        [Test]
        public void ConfirmationCannotRevealBeforeAllFourAndBotsNeverOperateLearner()
        {
            var c=new TutorialCourse(BattlefieldTests.Catalog(),1);
            c.Execute(CommandKind.SelectCard,"sabina-00-近身射击");c.Execute(CommandKind.SelectCard,"sabina-01-拔枪");
            c.Execute(CommandKind.CancelCardSelection);c.Execute(CommandKind.SelectCard,"sabina-01-拔枪");
            Assert.That(c.Execute(CommandKind.ConfirmCard).Accepted,Is.True);Assert.That(c.Session.View(0).Phase,Is.EqualTo(Phase.Planning));
            Assert.That(c.Line.Id,Is.EqualTo("select-confirm"));
            for(int i=0;i<6;i++)Assert.That(c.TickOpponent(),Is.True);
            Assert.That(c.Line.Id,Is.EqualTo("initiative"));Assert.That(c.Session.View(0).ActiveSeat,Is.EqualTo(0));
        }
        [Test]
        public void TutorialProgressIsVersionedAndCorruptDataFallsBackWithoutTouchingMatchSave()
        {
            string folder=Path.Combine(Path.GetTempPath(),"goa-tutorial-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"progress.json"),normal=Path.Combine(folder,"hotseat.json");
            try
            {
                File.WriteAllText(normal,"keep");var p=new TutorialProgress{ResumeChapter=3};p.Completed[0]=true;p.Write(path);
                var q=TutorialProgress.Read(path,out var message);Assert.That(message,Is.Empty);Assert.That(q.ResumeChapter,Is.EqualTo(3));Assert.That(q.Completed[0],Is.True);
                File.WriteAllText(path,"{\"Version\":999}");q=TutorialProgress.Read(path,out message);Assert.That(message,Is.Not.Empty);Assert.That(q.ResumeChapter,Is.Zero);
                File.WriteAllText(path,"not json");q=TutorialProgress.Read(path,out message);Assert.That(message,Is.Not.Empty);Assert.That(File.ReadAllText(normal),Is.EqualTo("keep"));
            }
            finally{Directory.Delete(folder,true);}
        }
    }
}
