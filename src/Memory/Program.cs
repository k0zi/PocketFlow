using OllamaSharp.Models.Chat;
using PocketFlow;
using Memory;

// ── Build the flow ────────────────────────────────────────────────────────────

var questionNode = new GetUserQuestionNode();
var retrieveNode = new RetrieveNode();
var answerNode   = new AnswerNode();
var embedNode    = new EmbedNode();

// Main path: question → retrieve → answer
questionNode.On("retrieve").Then(retrieveNode);
retrieveNode.On("answer").Then(answerNode);

// Archive path: when sliding window overflows
answerNode.On("embed").Then(embedNode);

// Loop back for next question
answerNode.On("question").Then(questionNode);
embedNode.On("question").Then(questionNode);

// ── Run ───────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('=', 50));
Console.WriteLine("PocketFlow Chat with Memory");
Console.WriteLine(new string('=', 50));
Console.WriteLine("This chat keeps your 3 most recent conversations");
Console.WriteLine("and brings back relevant past conversations when helpful");
Console.WriteLine("Type 'exit' to end the conversation");
Console.WriteLine(new string('=', 50));

var flow = new Flow(start: questionNode);
flow.Run(new Dictionary<string, object>());
