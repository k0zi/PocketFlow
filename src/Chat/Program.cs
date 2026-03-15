using PocketFlow;

// --- Build and run the flow ---
var chatNode = new ChatNode();
_ = (chatNode - "continue") >> chatNode; // Self-loop

var flow = new Flow(start: chatNode);
flow.Run(new Dictionary<string, object>());