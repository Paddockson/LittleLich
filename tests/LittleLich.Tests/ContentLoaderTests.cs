using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using Xunit;

namespace LittleLich.Tests;

public class ContentLoaderTests
{
    private static ContentDatabase Load(params (string path, string json)[] files) =>
        ContentLoader.CreateDefault().Load(files.Select(f => new ContentSource(f.path, f.json)));

    private static ContentLoadException LoadFails(params (string path, string json)[] files) =>
        Assert.Throws<ContentLoadException>(() => Load(files));

    [Fact]
    public void Shipped_data_folder_loads()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = ContentLoader.CreateDefault().Load(ContentSource.FromDirectory(dir));

        var mana = db.Get<ResourceDefinition>("mana");
        Assert.Equal("Mana", mana.Name);
        Assert.Equal("resources.json", mana.SourceFile);
        Assert.Equal("resource", mana.Type);
        Assert.True(mana.Placeholder);
        // Only these are tracked resources; everything gathered or crafted is an inventory item.
        Assert.Equal(new[] { "mana", "gold", "souls", "fire", "water", "air", "earth" }, db.All<ResourceDefinitionBase>().Select(r => r.Id));
        Assert.IsType<MaterialDefinition>(db.Get<ItemDefinitionBase>("iron_ore"));
    }

    [Fact]
    public void Array_form_with_type_per_entry()
    {
        var db = Load(("a.json", """[ { "type": "resource", "id": "bones", "name": "Bones", "base_cap": 50 } ]"""));
        Assert.Equal(50, db.Get<ResourceDefinition>("bones").BaseCap);
        Assert.Equal(1, db.Count);
    }

    [Fact]
    public void Get_missing_id_throws_with_name()
    {
        var db = Load(("a.json", """{ "type": "resource", "entries": [] }"""));
        var e = Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => db.Get<ResourceDefinition>("nope"));
        Assert.Contains("nope", e.Message);
        Assert.False(db.Contains<ResourceDefinition>("nope"));
    }

    [Fact]
    public void Unknown_field_reports_file_id_and_field()
    {
        var e = LoadFails(("res.json", """{ "type": "resource", "entries": [ { "id": "mana", "name": "Mana", "base_capp": 5 } ] }"""));
        var err = Assert.Single(e.Errors);
        Assert.Equal("res.json", err.File);
        Assert.Equal("mana", err.Id);
        Assert.Equal("base_capp", err.Field);
    }

    [Fact]
    public void Wrong_value_type_reports_field()
    {
        var e = LoadFails(("res.json", """{ "type": "resource", "entries": [ { "id": "mana", "name": "Mana", "base_cap": "lots" } ] }"""));
        var err = Assert.Single(e.Errors);
        Assert.Equal("base_cap", err.Field);
        Assert.Equal("mana", err.Id);
    }

    [Fact]
    public void Missing_name_and_bad_id_are_reported()
    {
        var e = LoadFails(("res.json", """
            { "type": "resource", "entries": [
                { "id": "mana" },
                { "id": "Iron Ore", "name": "Iron Ore" }
            ] }
            """));
        Assert.Contains(e.Errors, x => x.Id == "mana" && x.Field == "name");
        Assert.Contains(e.Errors, x => x.Id == "Iron Ore" && x.Field == "id");
    }

    [Fact]
    public void Missing_id_uses_entry_index()
    {
        var e = LoadFails(("res.json", """{ "type": "resource", "entries": [ { "name": "Mana" } ] }"""));
        var err = Assert.Single(e.Errors);
        Assert.Equal("#0", err.Id);
        Assert.Equal("id", err.Field);
    }

    [Fact]
    public void Unknown_and_missing_type()
    {
        var e = LoadFails(("x.json", """[ { "id": "a", "name": "A" }, { "type": "dragon", "id": "b", "name": "B" } ]"""));
        Assert.Contains(e.Errors, x => x.Id == "a" && x.Field == "type" && x.Message.Contains("missing"));
        Assert.Contains(e.Errors, x => x.Id == "b" && x.Field == "type" && x.Message.Contains("dragon"));
    }

    [Fact]
    public void Duplicate_ids_across_files_name_both_files()
    {
        var e = LoadFails(
            ("a.json", """[ { "type": "resource", "id": "mana", "name": "Mana" } ]"""),
            ("b.json", """[ { "type": "resource", "id": "mana", "name": "Mana 2" } ]"""));
        var err = Assert.Single(e.Errors);
        Assert.Equal("b.json", err.File);
        Assert.Contains("a.json", err.Message);
    }

    [Fact]
    public void Invalid_json_reports_file_and_line()
    {
        var e = LoadFails(("broken.json", "[\n { \"id\": \"a\", }\n ,, ]"));
        var err = Assert.Single(e.Errors);
        Assert.Equal("broken.json", err.File);
        Assert.Contains("line", err.Message);
    }

    [Fact]
    public void All_errors_are_collected_and_message_lists_them()
    {
        var e = LoadFails(
            ("a.json", "not json"),
            ("b.json", """[ { "type": "resource", "id": "x" } ]"""));
        Assert.Equal(2, e.Errors.Count);
        Assert.Contains("a.json", e.Message);
        Assert.Contains("b.json [x] .name", e.Message);
    }
}
