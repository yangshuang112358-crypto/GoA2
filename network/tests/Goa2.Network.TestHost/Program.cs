using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Goa2.Network;

// Separate test executable. Production serve has no save/scenario/Debug import endpoint.
if (args.Length != 4) throw new ArgumentException("root output scenario prefixSteps");
var content = ContentLoader.LoadDirectory(args[0]);
var definition = ScenarioRunner.Load(File.ReadAllText(args[2]));
var runner = new ScenarioRunner(content, definition);
int steps = int.Parse(args[3]);
for (int i = 0; i < steps; i++)
{
    runner.Next();
    if (!runner.Report.Passed) throw new InvalidOperationException("fixture prefix failed");
}
return await Goa2.Network.Program.RunServer(new Room(content, runner.Session), args[0], args[1]);
