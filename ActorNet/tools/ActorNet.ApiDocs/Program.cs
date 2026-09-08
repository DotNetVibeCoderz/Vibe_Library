// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.ApiDocs;

// Turns the XML the compiler already writes into markdown that sits beside the hand-written docs.
//
// The hand-written pages explain why something exists and when to reach for it; a generated
// reference answers "what is on this type" without anybody having to keep a second copy of every
// summary in step with the first. Neither replaces the other, which is why this writes to its own
// directory and links back rather than being folded in.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ActorNet.ApiDocs <output-directory> <assembly.xml> [<assembly.xml> ...]");
    return 2;
}

var output = args[0];
var inputs = args[1..];

var missing = inputs.Where(path => !File.Exists(path)).ToArray();
if (missing.Length > 0)
{
    // Named rather than skipped. A reference silently missing a whole assembly looks exactly like
    // an assembly with nothing public in it.
    Console.Error.WriteLine($"Not found: {string.Join(", ", missing)}");
    Console.Error.WriteLine("Build in Release first - the XML is written next to the assembly.");
    return 2;
}

var api = ApiModel.Read(inputs);
if (api.Namespaces.Count == 0)
{
    Console.Error.WriteLine("The XML held no documented types.");
    return 1;
}

var written = MarkdownWriter.Write(api, output);

Console.WriteLine($"{api.Namespaces.Count} namespace(s), {api.TypeCount} type(s), {api.MemberCount} member(s).");
foreach (var file in written) Console.WriteLine($"  {file}");

return 0;
