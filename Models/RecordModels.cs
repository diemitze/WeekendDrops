using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public enum RecordUnit
{
    Meters,
    Count,
    Roubles,
    Streak,
}


public class RecordDefinition
{
    public string Id { get; init; } = "";

    public RecordUnit Unit { get; init; }

    /// GP per unit of improvement.
    public double GpPerUnit { get; init; }

    /// Values below this never count as a record at all.
    public double Floor { get; init; }

    /// Improvements smaller than this are stored but pay nothing (avoids notification spam).
    public double MinStep { get; init; }

    /// Per-raid payout ceiling for this one record.
    public int GpCap { get; init; }

    /// Extract-only records: dying with a big number does not count.
    public bool RequiresSurvival { get; init; }
}

public class RecordEntry
{
    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("dateUtc")]
    public string DateUtc { get; set; } = "";

    [JsonPropertyName("beats")]
    public int Beats { get; set; }

    [JsonPropertyName("gpEarned")]
    public int GpEarned { get; set; }
}

public class RecordDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("previous")]
    public double Previous { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("dateUtc")]
    public string DateUtc { get; set; } = "";

    [JsonPropertyName("beats")]
    public int Beats { get; set; }

    [JsonPropertyName("gpEarned")]
    public int GpEarned { get; set; }
}

public class RecordsStateDto
{
    [JsonPropertyName("records")]
    public List<RecordDto> Records { get; set; } = [];

    [JsonPropertyName("totalBeats")]
    public int TotalBeats { get; set; }

    [JsonPropertyName("totalGpEarned")]
    public int TotalGpEarned { get; set; }
}
