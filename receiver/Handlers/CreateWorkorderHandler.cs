using contracts;

namespace receiver.Handlers;

public class CreateWorkorderHandler
{
    public void Handle(CreateWorkorder workorder)
    {
        Console.WriteLine("CreateWorkorderHandler");
    }
}