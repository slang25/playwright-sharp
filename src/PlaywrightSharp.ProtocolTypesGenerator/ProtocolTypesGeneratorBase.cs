using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PlaywrightSharp.ProtocolTypesGenerator
{
    internal abstract class ProtocolTypesGeneratorBase
    {
        protected abstract string Project { get; }

        protected string NamespacePrefix => $"PlaywrightSharp.{Project}.Protocol";

        protected string OutputDirectory => Path.Join("..", "..", "..", "..", $"PlaywrightSharp.{Project}", "Protocol");

        protected string OutputFile => Path.Join(OutputDirectory, "Protocol.Generated.cs");

        public async Task GenerateCodeAsync(RevisionInfo revision)
        {
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }

            var builder = new StringBuilder();
            builder.AppendAutoGeneratedPrefix();

            await GenerateTypesAsync(builder, revision).ConfigureAwait(false);

            builder.AppendAutoGeneratedSuffix();
            await File.WriteAllTextAsync(OutputFile, builder.ToString()).ConfigureAwait(false);
        }

        protected abstract Task GenerateTypesAsync(StringBuilder builder, RevisionInfo revision);

        protected void GenerateEventDefinition(StringBuilder builder, string eventName)
            => builder.AppendLine($"internal partial class {eventName}{Project}Event : I{Project}Event");

        protected void GenerateTypeDefinition(StringBuilder builder, string typeName)
            => builder.AppendLine($"internal partial class {typeName}");

        protected void GenerateRequestDefinition(StringBuilder builder, string typeName)
            => builder.AppendLine($"internal partial class {typeName}Request : I{Project}Request<{typeName}Response>");

        protected void GenerateResponseDefinition(StringBuilder builder, string typeName)
            => builder.AppendLine($"internal partial class {typeName}Response: I{Project}Response");
    }
}
