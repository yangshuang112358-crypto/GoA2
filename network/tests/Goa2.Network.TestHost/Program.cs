using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Goa2.Network;

// Separate test executable. Production serve has no save/scenario/Debug import endpoint.
if (args.Length != 4) throw new ArgumentException("root output scenario prefixSteps");
var content = ContentLoader.LoadDirectory(args[0]);
// Explicit legacy fixture for transport regressions; production Room always enables BP.
if(args[2]=="legacy")return await Goa2.Network.Program.RunServer(new Room(content,LocalGameFactory.Create(content,Guid.NewGuid().ToString("N"),new[]{"A","B","C","D"},17)),args[0],args[1]);
var definition = ScenarioRunner.Load(File.ReadAllText(args[2]));
var runner = new ScenarioRunner(content, definition);
int steps = int.Parse(args[3]);
for (int i = 0; i < steps; i++)
{
    var result = runner.Next();
    if (!result.Passed) throw new InvalidOperationException("fixture prefix failed: " + string.Join("; ", result.Errors));
}
return await Goa2.Network.Program.RunServer(new Room(content, runner.Session), args[0], args[1]);
