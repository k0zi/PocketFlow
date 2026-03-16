using PocketFlow;
using AsyncBasic;

// ── Build the flow (mirrors flow.py) ─────────────────────────────────────────

var fetch   = new FetchRecipes();
var suggest = new SuggestRecipe();
var approve = new GetApproval();
var end     = new AsyncNode(); // NoOp – terminates the flow

fetch.On("suggest").Then(suggest);
suggest.On("approve").Then(approve);
approve.On("retry").Then(suggest); // loop back for another suggestion
approve.On("accept").Then(end);     // graceful exit

var flow = new AsyncFlow(start: fetch);

// ── Run (mirrors main.py) ─────────────────────────────────────────────────────

var shared = new Dictionary<string, object>();

Console.WriteLine("\nWelcome to Recipe Finder!");
Console.WriteLine("------------------------");

await flow.RunAsync(shared);

Console.WriteLine("\nThanks for using Recipe Finder!");
