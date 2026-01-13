Console.WriteLine("put testing things here");
using (var fs = File.OpenRead(@"C:\Users\kakod\Desktop\from vrcsdk.txt"))
{
    using (var ms = new MemoryStream())
    {
        var L = fs.Length;
        fs.Position = L / 2;
        fs.CopyTo(ms);
        Console.WriteLine($"FS L is {L}, and MS L is {ms.Length}");
    }
}