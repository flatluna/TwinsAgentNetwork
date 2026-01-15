using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace TwinAgentsNetwork.Models
{
    public class TwinInfo
    {
        public string? Name { get; set; }
        public int? Age { get; set; }
        public string? Occupation { get; set; }

        public static JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(TwinInfo));
    }
    
}
