using System.Text.RegularExpressions;

namespace Logship.DeviceIdentifier.Components.Linux
{
    public sealed class DockerContainerIdAspect : BaseBashCommandAspect
    {
        private const string DockerContainer = "dockerContainer";
        private static readonly Regex dockerIdRegex = new("(\\d)+\\:(.)+?\\:(/.+?)??(/docker[-/])([0-9a-f]+)", RegexOptions.Multiline | RegexOptions.Compiled);

        protected override async ValueTask<Dictionary<string, string>> ExecuteAsync(CancellationToken token)
        {
            string result = await ReadFileAsync("/proc/1/cgroup", token);
            if (string.IsNullOrEmpty(result))
            {
                return new Dictionary<string, string>();
            }

            var match = dockerIdRegex.Match(result);
            if (!match.Success)
            {
                return new Dictionary<string, string>();
            }

            return new Dictionary<string, string>()
            {
                [DockerContainer] = match.Groups[5].Value
            };
        }
    }
}
