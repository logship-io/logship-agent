using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Logship.Agent.Core.Configuration.Validators.Attributes;

namespace Logship.Agent.Core.Configuration;

public sealed class LinuxIeee80211Configuration : BaseInputConfiguration
{
    [Required, RegularExpression("^[a-zA-Z0-9_.-]{1,15}$")]
    [JsonPropertyName("interface"), ConfigurationKeyName("interface")]
    public string Interface { get; set; } = string.Empty;

    [Required, RegularExpression("^[a-zA-Z0-9_.]+$")]
    [JsonPropertyName("schema"), ConfigurationKeyName("schema")]
    public string Schema { get; set; } = "Linux.80211.Management";

    [PositiveTimeSpan]
    [JsonPropertyName("channelDwell"), ConfigurationKeyName("channelDwell")]
    public TimeSpan ChannelDwell { get; set; } = TimeSpan.FromMilliseconds(250);

    [JsonPropertyName("frequenciesMHz"), ConfigurationKeyName("frequenciesMHz")]
    public List<int> FrequenciesMHz { get; set; } = [];
}
