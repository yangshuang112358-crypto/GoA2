using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
namespace Goa2.UI3D.Tests
{
    public sealed class MainFlowPolicyTests
    {
        [TestCase("initiative")][TestCase("defense")][TestCase("forced_discard")][TestCase("optional_discard")]
        [TestCase("minion_protection")][TestCase("recover_discard")][TestCase("discard_attack")][TestCase("card_swap")]
        [TestCase("attack_target")][TestCase("effect_target")][TestCase("effect_minion")][TestCase("effect_move")]
        [TestCase("placement")][TestCase("primary_option")][TestCase("gold_transfer")][TestCase("hero_respawn")]
        [TestCase("round_minion_removal")][TestCase("action_minion_removal")][TestCase("minion_spawn")]
        [TestCase("minion_return")][TestCase("spawn_order_unresolved")]
        public void PendingPromptsChooserNotSuspendedActor(string kind)
        {
            var v=new GameView{Phase=Phase.EffectChoice,ActiveSeat=0,Pending=new PendingChoice{Kind=kind,ChooserSeat=1}};
            Assert.That(MainFlowPolicy.NeedsInput(v,1),Is.True);
            Assert.That(MainFlowPolicy.NeedsInput(v,0),Is.False);
            Assert.That(MainFlowPolicy.Step(kind),Is.Not.EqualTo("完成当前选择"));
            Assert.That(MainFlowPolicy.Summary(v,s=>"英雄"+s),Does.StartWith("英雄1"));
        }
        [Test] public void SecretSelectionDoesNotChangePublicSummary()
        {
            var v=new GameView{Phase=Phase.Planning};v.Players.Add(new PlayerView{Seat=0});
            string before=MainFlowPolicy.Summary(v,s=>s.ToString());
            v.OwnCards.Add(new CardInstance{CardId="secret",Zone=CardZone.Selected});
            Assert.That(MainFlowPolicy.Summary(v,s=>s.ToString()),Is.EqualTo(before));
            Assert.That(MainFlowPolicy.Instruction(v,0),Does.Contain("确认本回合"));
            v.QuickSelection=true;Assert.That(MainFlowPolicy.NeedsInput(v,0),Is.False);
        }
        [Test] public void UpgradesOnlyPromptViewerWithCandidates()
        {
            var v=new GameView{Phase=Phase.RoundEnd,RoundEndStage="upgrades"};
            Assert.That(MainFlowPolicy.NeedsInput(v,0),Is.False);
            v.UpgradeOptions.Add(new UpgradeOption());Assert.That(MainFlowPolicy.NeedsInput(v,0),Is.True);
        }
        [Test] public void RoundEndResolutionIsAnAction()
        {
            var v=new GameView{Phase=Phase.RoundEnd,CanResolveRoundEnd=true};
            Assert.That(MainFlowPolicy.NeedsInput(v,0),Is.True);
            Assert.That(MainFlowPolicy.Instruction(v,0),Is.EqualTo("开始轮末结算"));
        }
        [Test] public void VictorySuppressesStalePendingPrompt()
        {
            var v=new GameView{Phase=Phase.Finished,Pending=new PendingChoice{Kind="defense",ChooserSeat=0}};
            Assert.That(MainFlowPolicy.NeedsInput(v,0),Is.False);
            Assert.That(MainFlowPolicy.Summary(v,s=>s.ToString()),Is.EqualTo("对局已结束"));
        }
    }
}
