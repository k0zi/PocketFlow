using OllamaSharp.Models.Chat;
using PocketFlow;

class ChatNode : Node
{
    protected override object? Prep(object shared)
    {
        var store = (Dictionary<string, object>)shared;

        // Initialize messages if this is the first run
        if (!store.ContainsKey("messages"))
        {
            store["messages"] = new List<Message>();
            Console.WriteLine("Welcome to the chat! Type 'exit' to end the conversation.");
        }

        // Get user input
        Console.Write("\nYou: ");
        var userInput = Console.ReadLine() ?? string.Empty;

        // Check if user wants to exit
        if (userInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            return null;

        // Add user message to history
        var messages = (List<Message>)store["messages"];
        messages.Add(new Message { Role = ChatRole.User, Content = userInput });

        return messages;
    }

    protected override object? Exec(object? prepRes)
    {
        if (prepRes is null) return null;

        var messages = (List<Message>)prepRes;
        return OllamaConnector.CallLlm(messages);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        if (prepRes is null || execRes is null)
        {
            Console.WriteLine("\nGoodbye!");
            return null; // End the conversation
        }

        var reply = (string)execRes;
        Console.WriteLine($"\nAssistant: {reply}");

        // Add assistant message to history
        var store = (Dictionary<string, object>)shared;
        var messages = (List<Message>)store["messages"];
        messages.Add(new Message { Role = ChatRole.Assistant, Content = reply });

        return "continue";
    }
}