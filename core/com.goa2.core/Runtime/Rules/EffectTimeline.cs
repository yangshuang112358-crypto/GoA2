#nullable enable
using System;
using Goa2.Domain;

namespace Goa2.Rules
{
    public static class EffectTimeline
    {
        public static EffectWindow? Create(int round, int turn, int turnsPerRound, EffectDuration duration)
        {
            if (round < 1 || turnsPerRound < 1 || turn < 1 || turn > turnsPerRound || !Enum.IsDefined(typeof(EffectDuration),duration))
                throw new ArgumentOutOfRangeException(nameof(turn), "效果持续时间需要有效轮次、回合和持续期。");
            if (duration == EffectDuration.NextTurn && turn == turnsPerRound) return null;
            int first = duration == EffectDuration.NextTurn ? turn + 1 : turn;
            int last = duration == EffectDuration.ThisRound ? turnsPerRound : first;
            return new EffectWindow { StartRound=round,StartTurn=first,EndRound=round,EndTurn=last };
        }
        private static int Compare(int leftRound, int leftTurn, int rightRound, int rightTurn) => leftRound != rightRound ? leftRound.CompareTo(rightRound) : leftTurn.CompareTo(rightTurn);
        public static bool Active(EffectWindow window, int round, int turn) => Compare(round,turn,window.StartRound,window.StartTurn) >= 0 && Compare(round,turn,window.EndRound,window.EndTurn) <= 0;
        public static bool EndsAtBoundary(EffectWindow window, int round, int turn) => Compare(round,turn,window.EndRound,window.EndTurn) >= 0;
    }
}
