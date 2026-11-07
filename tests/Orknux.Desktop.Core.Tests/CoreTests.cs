using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Orknux.Desktop.Core;

namespace Orknux.Desktop.Core.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("Enter", "Enter")]
    [InlineData("ctrl+s", "Ctrl+S")]
    [InlineData("Ctrl+Shift+N", "Ctrl+Shift+N")]
    [InlineData("Alt+F4", "Alt+F4")]
    [InlineData("Win+R", "Win+R")]
    [InlineData("control+alt+delete", "Ctrl+Alt+Delete")]
    [InlineData("ArrowDown", "Down")]
    [InlineData("esc", "Escape")]
    [InlineData("Ctrl++", "Ctrl++")]
    [InlineData("a", "A")]
    [InlineData("1", "1")]
    public void Reads_what_a_model_writes(string written, string read) =>
        Assert.Equal(read, KeyChord.Parse(written).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+S")]
    [InlineData("Ctrl+Banana")]
    public void Refuses_in_words_what_it_does_not_know(string written)
    {
        var refused = Assert.Throws<RefusedException>(() => KeyChord.Parse(written));
        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
    }
}

public class SnapshotTests
{
    private static SnapNode Window()
    {
        var root = new SnapNode { Role = "window", Name = "Untitled - Notepad" };
        var menu = new SnapNode { Role = "menubar", Name = "Application", Ref = "e1" };
        menu.Children.Add(new SnapNode { Role = "menuitem", Name = "File", Ref = "e2" });
        root.Children.Add(menu);
        root.Children.Add(new SnapNode { Role = "text", Name = "Ready" });
        root.Children.Add(new SnapNode { Role = "edit", Name = "Text editor", Value = "hello  world", Ref = "e3", States = ["focused"] });
        var button = new SnapNode { Role = "button", Name = "Save", Ref = "e4", States = ["disabled"] };
        button.Children.Add(new SnapNode { Role = "text", Name = "Save" });
        root.Children.Add(button);
        return root;
    }

    [Fact]
    public void Controls_carry_their_refs_and_text_stands_alone()
    {
        var lines = SnapshotRenderer.LinesOf(Window(), interactiveOnly: false);
        Assert.Equal(
            ["[e1] menubar \"Application\"", "[e2] menuitem \"File\"", "Ready",
             "[e3] edit \"Text editor\" value=\"hello world\" (focused)", "[e4] button \"Save\" (disabled)"],
            lines);
    }

    [Fact]
    public void Interactive_only_leaves_the_text_out()
    {
        var lines = SnapshotRenderer.LinesOf(Window(), interactiveOnly: true);
        Assert.DoesNotContain("Ready", lines);
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void Find_narrows_and_a_small_ceiling_cuts_on_a_line()
    {
        var lines = SnapshotRenderer.LinesOf(Window(), interactiveOnly: false);
        var found = SnapshotRenderer.Render(42, "Notepad", lines, "save", 20_000);
        Assert.Contains("[e4] button", found["text"]!.GetValue<string>());
        Assert.DoesNotContain("[e3]", found["text"]!.GetValue<string>());

        var cut = SnapshotRenderer.Render(42, "Notepad", lines, null, 60);
        Assert.True(cut["text"]!.GetValue<string>().Length <= 60);
        Assert.NotNull(cut["cut"]);

        var none = SnapshotRenderer.Render(42, "Notepad", lines, "kicad", 20_000);
        Assert.NotNull(none["note"]);
    }

    [Fact]
    public void A_ref_from_an_older_snapshot_is_refused()
    {
        var table = new RefTable<object>();
        var first = new object();
        Assert.Equal("e1", table.Add(first));
        Assert.Same(first, table.Resolve("E1"));
        Assert.Throws<RefusedException>(() => table.Resolve("e9"));
        Assert.Throws<RefusedException>(() => table.Resolve(""));
    }
}

/// <summary>
/// The connection against a stub of the server's desktop door: it says who it
/// is, takes a command from the long poll, runs it, and posts the answer - or
/// the refusal, in the model's words - to that command's id.
/// </summary>
public class PairingTests
{
    private sealed class PairStub : HttpMessageHandler
    {
        public readonly List<string> Asked = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Assert.Equal("/api/clients/pair", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(cancel);
            Asked.Add(body);
            var code = JsonNode.Parse(body)!["code"]!.GetValue<string>();
            return code.ToUpperInvariant().Replace("-", "") == "K7P4QX"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"credential":"orkxc_abc","user":"alice"}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"That pairing code is not valid."}""", Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("K7P-4QX", true)]
    [InlineData(" k7p4qx ", true)]
    [InlineData("orkx_abcdef", false)]
    [InlineData("orkxc_abc", false)]
    [InlineData("O1L-4QX", false)]
    public void Tells_a_code_from_a_token(string typed, bool code) => Assert.Equal(code, Pairing.IsCode(typed));

    [Theory]
    [InlineData("http://192.168.0.190:8098/pair/U7N-HQE", "http://192.168.0.190:8098/", "U7N-HQE")]
    [InlineData("https://orknux.example.com/pair/u7nhqe", "https://orknux.example.com/", "u7nhqe")]
    [InlineData("  https://example.com/orknux/pair/K7P-4QX/  ", "https://example.com/orknux", "K7P-4QX")]
    [InlineData("https://example.com/pair/x/pair/K7P-4QX", "https://example.com/pair/x", "K7P-4QX")]
    public void A_pairing_link_is_split_into_server_and_code(string link, string server, string code)
    {
        Assert.True(Pairing.TryParseLink(link, out var parsed, out var said));
        Assert.Equal(new Uri(server).ToString().TrimEnd('/'), parsed.ToString().TrimEnd('/'));
        Assert.Equal(code, said);
    }

    [Theory]
    [InlineData("K7P-4QX")]
    [InlineData("https://orknux.example.com")]
    [InlineData("https://orknux.example.com/pair/")]
    [InlineData("https://orknux.example.com/pair/O1L-4QX")]
    [InlineData("ftp://orknux.example.com/pair/K7P-4QX")]
    [InlineData("orkx_abcdef")]
    [InlineData("")]
    public void Anything_else_is_not_a_pairing_link(string text) => Assert.False(Pairing.TryParseLink(text, out _, out _));

    [Fact]
    public async Task A_code_is_traded_for_a_credential_as_Windows()
    {
        var stub = new PairStub();

        var credential = await Pairing.TradeAsync(new Uri("http://orknux.invalid/"), "k7p-4qx", "Windows on test", CancellationToken.None, stub);

        Assert.Equal("orkxc_abc", credential);
        var asked = JsonNode.Parse(Assert.Single(stub.Asked))!;
        Assert.Equal("Windows", asked["client"]!.GetValue<string>());
        Assert.Equal("Windows on test", asked["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_refused_code_is_refused_in_the_server_s_words()
    {
        var refused = await Assert.ThrowsAsync<ConnectionRefusedException>(
            () => Pairing.TradeAsync(new Uri("http://orknux.invalid"), "ZZZ-ZZZ", "x", CancellationToken.None, new PairStub()));

        Assert.Equal("That pairing code is not valid.", refused.Message);
    }
}

public class ConnectionTests
{
    private sealed class Stub : HttpMessageHandler
    {
        public readonly Queue<string> Commands = new();
        public readonly List<(string Path, string Body)> Posted = [];
        public string? Authorization;
        public bool Compatible = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Authorization = request.Headers.Authorization?.ToString();
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/client/compatibility")
                return Json(Compatible ? """{"compatible":true}""" : """{"compatible":false,"reason":"Needs a client of version 2."}""");
            if (path == "/api/desktop/hello") return Json("""{"user":"alice"}""");
            if (path == "/api/desktop/next")
            {
                lock (Commands)
                {
                    if (Commands.Count > 0) return Json(Commands.Dequeue());
                }
                await Task.Delay(20, cancel);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/desktop/answers/"))
            {
                var body = await request.Content!.ReadAsStringAsync(cancel);
                lock (Posted) Posted.Add((path, body));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Runner : ICommandRunner
    {
        public JsonNode Run(string action, JsonObject arguments) => action switch
        {
            "windows" => new JsonObject { ["windows"] = new JsonArray(new JsonObject { ["window"] = 7, ["title"] = "Notepad" }) },
            "press" => throw new RefusedException("Not a key."),
            _ => throw new InvalidOperationException("boom"),
        };
    }

    private static async Task<List<(string Path, string Body)>> Drive(Stub stub, int answers)
    {
        using var connection = new Connection(new Uri("http://orknux.invalid"), " orkx_token ", "1.0.0", new Runner(), stub);
        Assert.Equal("alice", await connection.HelloAsync(CancellationToken.None));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = connection.RunAsync(stop.Token);
        while (!stop.IsCancellationRequested)
        {
            lock (stub.Posted) if (stub.Posted.Count >= answers) break;
            await Task.Delay(20);
        }
        stop.Cancel();
        await running;
        return stub.Posted;
    }

    [Fact]
    public async Task A_command_is_run_and_its_answer_posted_to_its_id()
    {
        var stub = new Stub();
        stub.Commands.Enqueue("""{"id":"c1","action":"windows","arguments":{}}""");

        var posted = await Drive(stub, 1);

        Assert.Equal("Bearer orkx_token", stub.Authorization);
        var (path, body) = Assert.Single(posted);
        Assert.Equal("/api/desktop/answers/c1", path);
        Assert.Equal("Notepad", JsonNode.Parse(body)!["result"]!["windows"]![0]!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_refusal_and_a_fault_both_reach_the_model_as_words()
    {
        var stub = new Stub();
        stub.Commands.Enqueue("""{"id":"c1","action":"press","arguments":{"keys":"Hyper"}}""");
        stub.Commands.Enqueue("""{"id":"c2","action":"fly","arguments":{}}""");

        var posted = await Drive(stub, 2);

        Assert.Equal("Not a key.", JsonNode.Parse(posted[0].Body)!["error"]!.GetValue<string>());
        Assert.Contains("boom", JsonNode.Parse(posted[1].Body)!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_incompatible_server_is_refused_in_its_own_words()
    {
        var stub = new Stub { Compatible = false };
        using var connection = new Connection(new Uri("http://orknux.invalid"), "t", "1.0.0", new Runner(), stub);

        var refused = await Assert.ThrowsAsync<ConnectionRefusedException>(() => connection.HelloAsync(CancellationToken.None));

        Assert.Equal("Needs a client of version 2.", refused.Message);
    }

    private static readonly string[] StartMenu =
    [
        "Musigium", "Uninstall Musigium", "Musigium Setup", "MuseScore 4", "Notepad", "Notepad++",
        "IntelliJ IDEA 2026.2", "Windows Terminal", "Visual Studio Code", "KeePassXC",
    ];

    [Fact]
    public void An_app_is_found_by_the_name_a_person_would_type() =>
        Assert.Equal("Musigium", AppNames.Best("musigium", StartMenu));

    [Fact]
    public void The_app_comes_before_its_uninstaller_and_installer() =>
        Assert.Equal("Musigium", AppNames.Best("Musigium", StartMenu.Reverse()));

    [Fact]
    public void A_whole_name_beats_a_longer_one_that_starts_with_it() =>
        Assert.Equal("Notepad", AppNames.Best("notepad", StartMenu));

    [Fact]
    public void A_name_that_starts_with_what_was_asked_is_found() =>
        Assert.Equal("IntelliJ IDEA 2026.2", AppNames.Best("intellij", StartMenu));

    [Fact]
    public void A_word_inside_the_name_is_found() =>
        Assert.Equal("Visual Studio Code", AppNames.Best("studio code", StartMenu));

    [Fact]
    public void An_uninstaller_is_found_only_when_asked_for() =>
        Assert.Equal("Uninstall Musigium", AppNames.Best("uninstall musigium", StartMenu));

    [Fact]
    public void Shortcut_endings_and_case_do_not_matter() =>
        Assert.Equal("KeePassXC", AppNames.Best("keepassxc.lnk", ["KeePassXC.lnk".Replace(".lnk", ""), "Notepad"]));

    [Fact]
    public void Nothing_is_picked_for_a_name_no_app_has() =>
        Assert.Null(AppNames.Best("photoshop", StartMenu));

    [Fact]
    public void Near_names_are_offered_when_nothing_matched() =>
        Assert.Contains("MuseScore 4", AppNames.Near("musescore studio", StartMenu));
}
