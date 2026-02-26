using SQLite;

namespace FoodStreetMobile.Models;

[Table("poi_translations")]
public sealed class PoiTranslationEntity
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Indexed(Name = "ux_poi_lang", Unique = true, Order = 1)]
    [Column("poi_id")]
    public string PoiId { get; set; } = string.Empty;

    [Indexed(Name = "ux_poi_lang", Unique = true, Order = 2)]
    [Column("lang_code")]
    public string LangCode { get; set; } = "vi";

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("tts_text")]
    public string TtsText { get; set; } = string.Empty;
}
