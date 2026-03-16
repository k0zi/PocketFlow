using PocketFlow;

// --- Build and run the flow ---
var chatNode = new ChatNode();
chatNode.On("continue").Then(chatNode); // Self-loop

var flow = new Flow(start: chatNode);
flow.Run(new Dictionary<string, object>());