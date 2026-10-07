// Print the methods of one type in a .NET assembly, with parameter names and types.
// With a third argument, also list what each method of that name calls and which
// strings it uses, in order. Reads metadata and IL bytes only; nothing from the
// assembly is loaded or run.
// Usage: dotnet run -- <assembly.dll> <Namespace.TypeName> [MethodName]
using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("usage: InspectMethods <assembly.dll> <Namespace.TypeName> [MethodName]");
    return 1;
}

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var provider = new NameProvider();

foreach (var typeHandle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(typeHandle);
    var fullName = $"{md.GetString(type.Namespace)}.{md.GetString(type.Name)}";
    if (fullName != args[1]) continue;

    Console.WriteLine($"{Access(type.Attributes & System.Reflection.TypeAttributes.VisibilityMask)} {fullName}");
    foreach (var methodHandle in type.GetMethods())
    {
        var method = md.GetMethodDefinition(methodHandle);
        var sig = method.DecodeSignature(provider, null);
        var names = method.GetParameters()
            .Select(h => md.GetParameter(h))
            .Where(p => p.SequenceNumber > 0)
            .OrderBy(p => p.SequenceNumber)
            .Select(p => md.GetString(p.Name))
            .ToList();
        var parameters = sig.ParameterTypes.Select((t, i) => $"{t} {(i < names.Count ? names[i] : "?")}");
        var isStatic = (method.Attributes & System.Reflection.MethodAttributes.Static) != 0 ? "static " : "";
        var access = (method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask).ToString().ToLowerInvariant();
        Console.WriteLine($"  {access} {isStatic}{sig.ReturnType} {md.GetString(method.Name)}({string.Join(", ", parameters)})");
        if (args.Length == 3 && md.GetString(method.Name) == args[2] && method.RelativeVirtualAddress != 0)
        {
            foreach (var line in DumpCallsAndStrings(pe, md, method, provider))
                Console.WriteLine($"      {line}");
        }
    }
    return 0;
}
Console.Error.WriteLine($"type {args[1]} not found");
return 1;

// Walk the IL and report call targets, field accesses, argument loads and string
// literals. Enough to follow what a method does without a full decompiler.
static IEnumerable<string> DumpCallsAndStrings(PEReader pe, MetadataReader md, MethodDefinition method, NameProvider provider)
{
    var operandTypes = typeof(OpCodes).GetFields()
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => (ushort)op.Value, op => op.OperandType);
    var reader = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
    var paramNames = method.GetParameters().Select(h => md.GetParameter(h))
        .Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber).Select(p => md.GetString(p.Name)).ToList();
    var isStatic = (method.Attributes & System.Reflection.MethodAttributes.Static) != 0;

    while (reader.RemainingBytes > 0)
    {
        int offset = reader.Offset;
        ushort code = reader.ReadByte();
        if (code == 0xFE) code = (ushort)(0xFE00 | reader.ReadByte());
        var operand = operandTypes.TryGetValue(code, out var t) ? t : OperandType.InlineNone;
        var name = ((ILOpCode)code).ToString().ToLowerInvariant();

        string? arg = code switch
        {
            0x02 or 0x03 or 0x04 or 0x05 => ArgName((code - 0x02), paramNames, isStatic),
            _ => null,
        };
        if (arg != null) { yield return $"IL_{offset:x4} {name} ({arg})"; continue; }

        switch (operand)
        {
            case OperandType.InlineNone:
                if (name.StartsWith("ldloc") || name.StartsWith("stloc") || name is "ret" or "endfinally" or "pop")
                    yield return $"IL_{offset:x4} {name}";
                break;
            case OperandType.ShortInlineBrTarget:
                var shortTarget = reader.ReadSByte() + reader.Offset;
                yield return $"IL_{offset:x4} {name} -> IL_{shortTarget:x4}";
                break;
            case OperandType.ShortInlineI: reader.ReadSByte(); break;
            case OperandType.ShortInlineVar:
                var idx = reader.ReadByte();
                if (code is 0x0E or 0x0F or 0x10) yield return $"IL_{offset:x4} {name} ({ArgName(idx, paramNames, isStatic)})";
                else yield return $"IL_{offset:x4} {name} {idx}";
                break;
            case OperandType.InlineVar: reader.ReadUInt16(); break;
            case OperandType.InlineBrTarget:
                var target = reader.ReadInt32() + reader.Offset;
                yield return $"IL_{offset:x4} {name} -> IL_{target:x4}";
                break;
            case OperandType.InlineI: reader.ReadInt32(); break;
            case OperandType.InlineI8: reader.ReadInt64(); break;
            case OperandType.ShortInlineR: reader.ReadSingle(); break;
            case OperandType.InlineR: reader.ReadDouble(); break;
            case OperandType.InlineSwitch:
                var count = reader.ReadInt32();
                for (int i = 0; i < count; i++) reader.ReadInt32();
                break;
            case OperandType.InlineString:
                var str = md.GetUserString(MetadataTokens.UserStringHandle(reader.ReadInt32()));
                yield return $"IL_{offset:x4} {name} \"{str}\"";
                break;
            default:
                var token = reader.ReadInt32();
                if (operand is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)
                    yield return $"IL_{offset:x4} {name} {MemberName(md, MetadataTokens.EntityHandle(token), provider)}";
                break;
        }
    }
}

static string ArgName(int index, List<string> names, bool isStatic)
{
    if (!isStatic) { if (index == 0) return "this"; index--; }
    return index < names.Count ? names[index] : $"arg{index}";
}

static string MemberName(MetadataReader md, EntityHandle handle, NameProvider provider)
{
    string TypeOf(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name),
        HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name),
        _ => "?",
    };
    switch (handle.Kind)
    {
        case HandleKind.MethodDefinition:
            var def = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            return $"{TypeOf(def.GetDeclaringType())}::{md.GetString(def.Name)}";
        case HandleKind.MemberReference:
            var mref = md.GetMemberReference((MemberReferenceHandle)handle);
            return $"{TypeOf(mref.Parent)}::{md.GetString(mref.Name)}";
        case HandleKind.FieldDefinition:
            var field = md.GetFieldDefinition((FieldDefinitionHandle)handle);
            return $"{TypeOf(field.GetDeclaringType())}::{md.GetString(field.Name)}";
        case HandleKind.MethodSpecification:
            var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
            return MemberName(md, spec.Method, provider) + "<...>";
        default:
            return handle.Kind.ToString();
    }
}

static string Access(System.Reflection.TypeAttributes v) => v switch
{
    System.Reflection.TypeAttributes.Public => "public",
    System.Reflection.TypeAttributes.NotPublic => "internal",
    _ => v.ToString().ToLowerInvariant(),
};

sealed class NameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString().ToLowerInvariant();
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte kind) => r.GetString(r.GetTypeDefinition(h).Name);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte kind) => r.GetString(r.GetTypeReference(h).Name);
    public string GetSZArrayType(string element) => element + "[]";
    public string GetArrayType(string element, ArrayShape shape) => element + "[,]";
    public string GetByReferenceType(string element) => "ref " + element;
    public string GetPointerType(string element) => element + "*";
    public string GetGenericInstantiation(string generic, ImmutableArray<string> args) => $"{generic}<{string.Join(",", args)}>";
    public string GetGenericTypeParameter(object? ctx, int index) => $"T{index}";
    public string GetGenericMethodParameter(object? ctx, int index) => $"M{index}";
    public string GetFunctionPointerType(MethodSignature<string> sig) => "fnptr";
    public string GetModifiedType(string modifier, string unmodified, bool required) => unmodified;
    public string GetPinnedType(string element) => element;
    public string GetTypeFromSpecification(MetadataReader r, object? ctx, TypeSpecificationHandle h, byte kind) => "typespec";
}
