#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class ReplayImportTests
    {
        private static readonly JsonStateCodec Codec = new JsonStateCodec();
        private static GameState Origin(ContentCatalog catalog, GameState saved)
        {
            var state = new GameRules().Create(catalog,saved.MatchId,saved.Players.Select(p=>p.Name).ToArray(),saved.Seed,saved.InitialEngineVersion);
            state.Sandbox=saved.Sandbox; state.QuickSelection=saved.Sandbox;
            return state;
        }
        [Test]
        public void IsolatedImportMatchesLivePendingChoiceAndDoesNotBorrowInputObjects()
        {
            var catalog=BattlefieldTests.Catalog();
            var live=OptionalAttackDiscardTests.Ready(catalog,OptionalAttackDiscardTests.Axe,3,reflection:true);
            Apply(live,0,CommandKind.BeginPrimary); Apply(live,0,CommandKind.ChooseOptionalDiscard,OptionalAttackDiscardTests.Cost);
            Apply(live,0,CommandKind.ChooseAttackTarget,"hero:1"); Apply(live,1,CommandKind.Defend,"wasp-10-反射屏障");
            var saved=Codec.Read(live.ExportSave()); var initial=Origin(catalog,saved); string original=Codec.Write(initial);
            var imported=GameSession.Replay(catalog,Codec,initial,saved.AcceptedCommands);
            Assert.That(imported.ExportSave(),Is.EqualTo(live.ExportSave()));
            Assert.That(Codec.Write(initial),Is.EqualTo(original));
            Assert.That(imported.View(null).Pending!.Kind,Is.EqualTo("forced_discard"));
            Assert.That(imported.View(null).ForcedDiscardCards,Is.Empty);
            Assert.That(imported.View(0).ForcedDiscardCards,Does.Not.Contain(OptionalAttackDiscardTests.Cost));
            Assert.That(imported.View(0).Pending!.Source,Is.Empty);
            Assert.That(imported.View(1).Pending!.Source,Is.EqualTo("wasp-10-反射屏障"));
            string before=imported.ExportSave();
            initial.Players.Clear(); saved.AcceptedCommands[0].Value="changed"; saved.AcceptedCommands.Clear();
            var projection=imported.View(0); projection.OwnCards.Clear(); projection.Events.Clear();
            Assert.That(imported.ExportSave(),Is.EqualTo(before));
            string choice=imported.View(0).ForcedDiscardCards.First();
            var command=Cmd(imported,0,CommandKind.ForcedDiscard,choice);
            Assert.That(imported.Execute(0,command).Accepted,Is.True);
            string completed=imported.ExportSave();
            Assert.That(imported.Execute(0,command).Duplicate,Is.True);
            Assert.That(imported.ExportSave(),Is.EqualTo(completed));
            Assert.That(LocalGameFactory.Restore(catalog,completed).ExportSave(),Is.EqualTo(completed));
        }
        [TestCase("duplicate")]
        [TestCase("conflict")]
        [TestCase("stale")]
        [TestCase("identity")]
        [TestCase("match")]
        [TestCase("rule")]
        public void InvalidHistoryReturnsNoSessionAndLeavesTheOriginUnchanged(string failure)
        {
            var catalog=FoundationTests.Fixture(); var live=Planning(); var saved=Codec.Read(live.ExportSave());
            var initial=Origin(catalog,saved); string before=Codec.Write(initial);
            var invalid=new Command {Id="bad",MatchId=saved.MatchId,ExpectedRevision=saved.Revision,ActorSeat=0,Kind=CommandKind.SelectCard,Value="hero0-gold"};
            switch(failure)
            {
                case "duplicate": invalid=saved.AcceptedCommands[0]; break;
                case "conflict": invalid.Id=saved.AcceptedCommands[0].Id; break;
                case "stale": invalid.ExpectedRevision--; break;
                case "identity": invalid.ActorSeat=4; break;
                case "match": invalid.MatchId="other-match"; break;
                case "rule": invalid.Value="missing-card"; break;
            }
            saved.AcceptedCommands.Add(invalid); GameSession? imported=null;
            var error=Assert.Throws<RuleViolation>(()=>imported=GameSession.Replay(catalog,Codec,initial,saved.AcceptedCommands));
            Assert.That(error!.Code,Is.EqualTo("invalid_replay")); Assert.That(imported,Is.Null);
            Assert.That(Codec.Write(initial),Is.EqualTo(before));
        }
        [Test]
        public void ImportRejectsAnAlreadyAdvancedOrigin()
        {
            var catalog=FoundationTests.Fixture(); var state=Codec.Read(Planning().ExportSave());
            var error=Assert.Throws<RuleViolation>(()=>GameSession.Replay(catalog,Codec,state,Array.Empty<Command>()));
            Assert.That(error!.Code,Is.EqualTo("invalid_replay_origin"));
        }
        [Test]
        public void ImportedSessionKeepsLiveRejectionsAtomicAndPrivate()
        {
            var catalog=FoundationTests.Fixture(); var live=Planning(); var saved=Codec.Read(live.ExportSave());
            var imported=GameSession.Replay(catalog,Codec,Origin(catalog,saved),saved.AcceptedCommands);
            string before=imported.ExportSave(); var command=Cmd(imported,0,CommandKind.SelectCard,"hero0-gold");
            Assert.That(imported.Execute(1,command).Code,Is.EqualTo("unauthorized"));
            Assert.That(imported.Execute(1,command).View.OwnCards.All(c=>c.CardId.StartsWith("hero1-",StringComparison.Ordinal)),Is.True);
            Assert.That(imported.Execute(4,command).View.OwnCards,Is.Empty);
            command.ExpectedRevision--;
            Assert.That(imported.Execute(0,command).Code,Is.EqualTo("stale_revision"));
            Assert.That(imported.ExportSave(),Is.EqualTo(before));
            Apply(imported,0,CommandKind.SelectCard,"hero0-gold");
            Assert.That(imported.View(1).Players[0].Revealed,Is.Empty);
        }
    }
}
