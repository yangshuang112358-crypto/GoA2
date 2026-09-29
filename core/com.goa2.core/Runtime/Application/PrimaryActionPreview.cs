#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Rules;

namespace Goa2.Application
{
    public sealed partial class GameSession
    {
        private long previewRevision=-1;
        private int previewSeat=-1;
        private PrimaryActionPreview? cachedPrimaryPreview;
        private PrimaryActionPreview? PreviewForView(GameState source,int seat)
        {
            if(previewRevision!=source.Revision || previewSeat!=seat)
            {
                cachedPrimaryPreview=PreviewPrimary(source,seat);previewRevision=source.Revision;previewSeat=seat;
            }
            var p=cachedPrimaryPreview;
            return p==null ? null : new PrimaryActionPreview {Kind=p.Kind,Optional=p.Optional,Targets=p.Targets.ToList(),Cells=p.Cells.ToList()};
        }
        // Simulate only on a private copy. A preview is permitted only when opening
        // the action has made no gameplay changes and no before-action frame exists.
        private PrimaryActionPreview? PreviewPrimary(GameState source,int seat)
        {
            if(source.Phase!=Phase.Action || source.ActiveSeat!=seat || source.Pending!=null || source.Execution!=null)return null;
            string original=codec.Write(source);
            var draft=codec.Read(original);
            try { rules.Apply(catalog,draft,new Command {Kind=CommandKind.BeginPrimary,ActorSeat=seat,Id="preview"}); }
            catch(RuleViolation){return null;}
            var pending=draft.Pending;
            if(pending==null || pending.ChooserSeat!=seat || draft.BeforeAction!=null)return null;
            var preview=new PrimaryActionPreview {Kind=pending.Kind,Optional=pending.Optional};
            switch(pending.Kind)
            {
                case "attack_target": preview.Targets=CombatRules.AttackTargets(catalog,draft,seat);break;
                case "effect_move": preview.Cells=GameRules.LegalEffectMoves(catalog,draft,seat).Select(m=>m.Destination).ToList();break;
                case "placement": preview.Cells=GameRules.LegalPlacements(catalog,draft,seat);break;
                default:return null;
            }
            var harmless=new[]{"PrimaryActionStarted","AttackRangeDetermined","AttackTargetChoiceRequired","EffectMoveChoiceRequired","PlacementChoiceRequired"};
            if(draft.Events.Skip(source.Events.Count).Any(e=>!harmless.Contains(e.Kind)))return null;
            draft.Execution=source.Execution;draft.Pending=source.Pending;draft.Phase=source.Phase;draft.Events=source.Events;
            return codec.Write(draft)==original ? preview : null;
        }
        private void ApplyWithPrimaryChoice(GameState draft,Command command)
        {
            string expected=command.Kind==CommandKind.CommitPrimaryAttack ? "attack_target" : command.Kind==CommandKind.CommitPrimaryMove ? "effect_move" : command.Kind==CommandKind.CommitPrimaryPlacement ? "placement" : "";
            if(expected==""){rules.Apply(catalog,draft,command);return;}
            var preview=PreviewPrimary(draft,command.ActorSeat);
            if(preview==null || preview.Kind!=expected)throw new RuleViolation("primary_preview_changed","当前主要行动需要先处理前置步骤，请重新选择。");
            var opening=new Command {Id=command.Id,MatchId=command.MatchId,ActorSeat=command.ActorSeat,ExpectedRevision=command.ExpectedRevision,Kind=CommandKind.BeginPrimary};
            rules.Apply(catalog,draft,opening);
            var choice=new Command {Id=command.Id,MatchId=command.MatchId,ActorSeat=command.ActorSeat,ExpectedRevision=command.ExpectedRevision,Value=command.Value,Destination=command.Destination,
                Kind=expected=="attack_target" ? CommandKind.ChooseAttackTarget : expected=="effect_move" ? CommandKind.ChooseEffectMove : CommandKind.ChoosePlacement};
            // Both steps share the existing transaction, receipt and revision. A bad
            // target rejects the entire draft; no public half-started action remains.
            rules.Apply(catalog,draft,choice);
        }
    }
}
