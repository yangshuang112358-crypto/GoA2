#nullable enable
using System;
using System.Collections.Generic;
using Goa2.Domain;

namespace Goa2.Infrastructure.Scenarios
{
    [Serializable]
    public sealed class ScenarioDefinition
    {
        public int SchemaVersion;
        public string Id = "", Name = "";
        public int Seed = 42;
        public bool Sandbox = true;
        public string[] Players = { "玩家1", "玩家2", "玩家3", "玩家4" };
        public bool VerifyReplayAfterEachStep = true;
        public List<ScenarioStep> Steps = new List<ScenarioStep>();
    }
    [Serializable]
    public sealed class ScenarioStep
    {
        public string Name = "", Actor = "p1", Command = "", Value = "", Target = "none", MoveMode = "Secondary";
        public string? AuthenticateAs;
        public Hex? Destination;
        public int? ReplayStep;
        public ScenarioExpectation Expect = new ScenarioExpectation();
    }
    [Serializable]
    public sealed class ScenarioExpectation
    {
        public string Code = "ok";
        public string? Phase, Active, PendingKind, PendingChooser;
        public int? Round, Turn, Revealed;
        public Dictionary<string, int> Gold = new Dictionary<string, int>();
        public Dictionary<string, int> HandCounts = new Dictionary<string, int>();
        public Dictionary<string, int> DiscardCounts = new Dictionary<string, int>();
        public Dictionary<string, Hex> Positions = new Dictionary<string, Hex>();
        public Dictionary<string, int> EventCounts = new Dictionary<string, int>();
        public List<string> EventOrder = new List<string>();
    }
    [Serializable]
    public sealed class ScenarioStepResult
    {
        public int Number;
        public string Name = "", Code = "", StateHash = "", Phase = "";
        public bool Accepted, Duplicate, Passed, Restored;
        public long Revision;
        public int Round, Turn;
        public Command? Command;
        public List<string> Errors = new List<string>();
    }
    [Serializable]
    public sealed class ScenarioReport
    {
        public string Id = "", Name = "", ContentHash = "", RulesVersion = "", ProtocolVersion = "", FinalStateHash = "";
        public int Seed, TotalSteps;
        public bool Complete, Passed;
        public List<ScenarioStepResult> Steps = new List<ScenarioStepResult>();
    }
}
