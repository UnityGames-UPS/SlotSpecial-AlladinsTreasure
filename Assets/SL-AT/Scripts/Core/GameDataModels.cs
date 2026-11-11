using System;
using System.Collections.Generic;

#region Server Communication Models — Init

[Serializable]
public class InitData
{
    public string id = "initData";
    public ServerGameData gameData;
    public ServerFeatures features;
    public ServerUIData uiData;
    public ServerPlayer player;
    // Wild Jokers sends no jackpot block. Kept because the platform's separate "jackpot:sync"
    // event reuses these types, and the init-time read is already null-guarded.
    public JackpotData jackpotData;
}

[Serializable]
public class JackpotData
{
    public JackpotValues values;
}

[Serializable]
public class JackpotValues
{
    public string miniJackpot;
    public string minorJackpot;
    public string majorJackpot;
    public string grandJackpot;
}

[Serializable]
public class JackpotSyncData
{
    public string gameId;
    public JackpotValues values;
}

// The four platform jackpot tiers, in the order the platform ranks them.
public enum JackpotTier
{
    Grand,
    Major,
    Minor,
    Mini
}

// Clicking a jackpot tier asks the platform to open its own jackpot overlay. Fire-and-forget —
// the server sends no direct reply; any resulting value change arrives on the usual jackpot:sync.
[Serializable]
public class JackpotOpenRequest
{
    public string type = "JACKPOT_OPEN";
    public JackpotOpenPayload payload = new JackpotOpenPayload();
}

[Serializable]
public class JackpotOpenPayload
{
    public string tier;
}

[Serializable]
public class ServerGameData
{
    // Empty: Aladdin's Treasure pays 243 ways, not lines. Still read for the reel count, which falls
    // back to 5.
    public List<List<int>> lines;
    public List<double> bets;
    // The bet multiplier: total bet = the selected bet x this (50). Confirmed: at bet 0.01 a losing
    // spin takes exactly 0.50 off the balance, and netReturnRatio is grandTotalWin / 0.50.
    public int creditDivisor;
    // The number of ways (243). Display only — it is not the bet multiplier.
    public int totalLines;
}

[Serializable]
public class ServerFeatures
{
    public ServerScatterFeature scatter;
    public ServerWildFeature wild;
    public ServerFreeGamesFeature freeGames;
}

// Every feature names its symbol. The ids are nullable so a missing one reads as "not sent" rather
// than 0, which is a real symbol (the Fairy).
[Serializable]
public class ServerScatterFeature
{
    public bool enabled;
    public int? symbolId;          // the BlueOrb
    // 3 BlueOrbs trigger free games. Whether they must sit on separate reels is not stated.
    public int minTrigger;
}

[Serializable]
public class ServerWildFeature
{
    public int? symbolId;
    // Zero-based reels the Wild can land on. [1,2,3] = reels 2-4 in the base game; [1,2,3,4] = reels
    // 2-5 in free games.
    public List<int> mainGameReels;
    public List<int> freeGameReels;
    // "enabled" is sent and deliberately unbound: the Wild has no round to switch off.
}

[Serializable]
public class ServerFreeGamesFeature
{
    public bool enabled;
    public int awardedOnTrigger;   // 10
    public int awardedOnRetrigger; // 3
    public double payMultiplier;   // 1
    public int maxTotalFreeGames;  // 300
    public ServerFramesFeature frames;
}

// The free-games "frames" mechanic. Its rules are not known yet; the values are bound so they are
// ready once they are.
[Serializable]
public class ServerFramesFeature
{
    public ServerFrameEndPrizes endPrizes;
    public double orangeHitMultiplier; // 10
}

[Serializable]
public class ServerFrameEndPrizes
{
    public double blue;   // 2
    public double purple; // 3
    public double orange; // 5
}

[Serializable]
public class ServerUIData
{
    // The name is inherited from the payline backends; it holds the symbol table.
    public PaylineData paylines;
}

[Serializable]
public class PaylineData
{
    public List<ServerSymbolInfo> symbols;
}

[Serializable]
public class ServerSymbolInfo
{
    public int id;
    public string name;         // "Fairy", "Wild", "BlueOrb", ... — display only, never used to find a role.
                                // Beware: id 9 is named "10" and id 10 is named "9".
    // "high", "low", "wild" or "scatter". This is what tells the special symbols apart; the init
    // sends no isSpecialSymbol flag and no displayName. "description" only repeats the group.
    public string group;
    // The fewest matching reels that pay: 3 for every symbol, including the Wild and the BlueOrb,
    // which have no paytable. Only cross-checked — the converter derives it from the paytable.
    public int minMatch;
    // The paytable, ASCENDING from minMatch and ENDING at a full-reel match: with 5 reels, index
    // 0 = 3-of-a-kind, 1 = 4, 2 = 5. In credits; the cash value of a credit is not confirmed yet.
    // Empty on the Wild and the BlueOrb. The converter reverses it, because the client works
    // descending from a full-reel match.
    public List<double> payout;
}

[Serializable]
public class ServerPlayer
{
    public double balance;
}

#endregion

#region Server Communication Models — Spin Result

[Serializable]
public class ServerSpinResponse
{
    public string id = "ResultData";
    public bool success;
    // Row-major and top-level, NOT nested under payload: 3 rows x 5 columns, values as strings.
    public List<List<string>> matrix;
    public ServerPayload payload;
    public ServerPlayerBalance player;
}

[Serializable]
public class ServerPlayerBalance
{
    // Nullable in case the server ever omits it; the converter falls back to a computed balance.
    public double? balance;
}

[Serializable]
public class ServerPayload
{
    // Equals grandTotalWin on every capture. grandTotalWin is the figure to display.
    public double winAmount;
    // One entry per winning SYMBOL, not per way. An empty list (not a missing field) on a losing
    // spin. Wins can share cells: a Wild serving two symbols is in both entries' matchedPositions.
    // Entry order is not known to mean anything; nothing should depend on it.
    public List<ServerWaysWin> waysWins;

    // BlueOrbs anywhere on the board, cells not reels. A lone BlueOrb pays nothing and has no
    // waysWins entry. Bound for the free-games step; nothing reads it yet.
    public int scatterCount;

    // The spin's total — the sum of the waysWins entries — and the only figure to display. The
    // balance moves by exactly this minus the total bet (bet x 50): 0.30 on a 0.50 bet took the
    // balance down 0.20.
    public double grandTotalWin;

    // The near-miss respin, decided by the server in this same result: the board landed before the
    // respin and which reel was respun. Null when the spin had none. "matrix" is the board AFTER it.
    public ServerNearMissRespin nearMissRespin;
    // True / 1 on a spin that carried a respin. Bound for completeness; nearMissRespin is what the
    // client acts on.
    public bool isRespin;
    public int respinCount;

    // Free Spins' round state. Sent on the spin that triggers and on every spin of the round; an
    // ordinary base spin has no block at all, which reads as "not in a round" (BonusFreeSpins.md §9.4).
    public ServerFreeSpinsResult freeSpins;

    // The Sandbell Pick, whole, on the spin that triggers it — the pick is never sent to the server
    // (SandbellPickBonus.md §8.2). Absent on every other spin.
    public ServerSandbellPickBonus sandbellPickBonus;

    // The Ball Gale Pick, whole, on the spin that triggers it: every pick, in order, decided with the
    // spin — each click only chooses which gem shows the next one (BallGalePickBonus.md §9.2).
    public ServerBallPickemBonus ballPickemBonus;

    // Sent and deliberately unbound:
    //  - "betAmountPerLine" and "totalBetAmount" (0.01 and 0.2 at the lowest bet): the client
    //    already knows its own bet. Sent on free spins too, where nothing is charged.
    //  - "netReturnRatio" (grandTotalWin over total bet): the controller derives the same ratio.
}

[Serializable]
public class ServerBallPickemBonus
{
    public string featureName;     // "ballPickemBonus"
    public bool triggered;
    // The picks in the order the player makes them; the last has isEnd (and isCollect).
    public List<ServerBallPick> picks;
    // The picks' values summed (× total bet).
    public double totalWinMultiplier;
    // The prize in cash: totalWinMultiplier × total bet. TOTAL WIN.
    public double winInCash;

    // Sent and deliberately unbound: "grid", "gridDimensions" and "centerBox" (the backend says ignore
    // them; "grid" is to be removed), and "triggerPositions" (the Balls' boxes come from the symbol id,
    // as the Drum's do).
}

[Serializable]
public class ServerBallPick
{
    public int pickIndex;
    // An X value from availablePrizes, × total bet; 0 on the COLLECT pick.
    public double multiplier;
    // The last pick: picking ends here.
    public bool isEnd;
    // The COLLECT pick, worth nothing. Always equal to isEnd (backend); each is read for its own job.
    public bool isCollect;

    // "revealedPrize" (= multiplier) and "revealed" ("x15" / "end") are sent and deliberately unbound.
}

[Serializable]
public class ServerSandbellPickBonus
{
    public string featureName;     // "sandbellPickBonus"
    public bool triggered;
    // The winning value, on a cell of the server's choosing — the client puts it on whichever
    // Sandbell the player clicks.
    public ServerPickPosition pickedPosition;
    // One per other Sandbell on the board, stacked ones included: the values the player missed.
    public List<ServerPickPosition> unpickedPositions;
    // The winning value again (× total bet). "prizeMultiplier" is sent too, always equal, and left
    // unbound.
    public double totalWinMultiplier;
    // The prize in cash: winning value × total bet. TOTAL WIN.
    public double winInCash;
}

[Serializable]
public class ServerPickPosition
{
    public int row;
    public int col;
    // An X value from availablePrizes, × total bet.
    public double multiplier;
}

[Serializable]
public class ServerNearMissRespin
{
    public bool triggered;
    // Zero-based reel that was respun: 3 for the Drum, 4 for the Ball and the Sandbell.
    public int reelIndex;
    // Row-major like matrix: the board before the respin. Only reel reelIndex differs from matrix.
    public List<List<string>> originalMatrix;
    // Which feature the near miss was for: "freeSpins", "sandbellPickBonus" — and "ballPickem" for
    // the Ball, which doesn't match its init key (reported).
    public string featureName;

    // "newMatrix" (the board after the respin) is deliberately unbound: it is not always sent, and
    // the top-level matrix is always that board.
}

[Serializable]
public class ServerFreeSpinsResult
{
    // Spins were awarded ON THIS SPIN: the trigger, or a retrigger inside the round.
    public bool triggered;
    // This spin was played inside the round: false on the base-game spin that triggers, true on
    // every free spin. What the round's last spin sends is not captured yet.
    public bool active;
    // Spins left after this one. A retrigger's spins are already folded in, so it can go up.
    public int spinsRemaining;
    // Spins awarded in the round so far, retriggers included.
    public int totalSpins;
    // The round's running total in cash, this spin included. Excludes the trigger spin's own win,
    // which was paid on that spin.
    public double totalWinCash;
}

// One winning symbol. Ways pay left to right from reel 1, on adjacent reels, 3 reels or more; a Wild
// (reels 2-4 in the base game) stands in for the symbol on its reel.
[Serializable]
public class ServerWaysWin
{
    // The symbol that pays — never the Wild, which has no paytable.
    public int symbolId;
    // Reels matched from the left: 3 to 5.
    public int matchCount;
    // The number of ways: the product of the matching cells on each matched reel, a Wild counting
    // as one more. Reel 1 with two of the symbol, then one each = 2 ways.
    public int waysCount;
    // EVERY matching cell, Wilds included, ordered by reel then row — not one per reel, and not
    // one list per way. 2 ways over 5 reels sent 6 cells.
    public List<ServerCell> matchedPositions;
    // 1 on every base-game capture, Wild wins included. Already inside winInCash.
    public double appliedMultiplier;
    // The win in cash: basePayout x waysCount x appliedMultiplier credits, one credit being the
    // selected bet. Confirmed: 25 x 2 x 1 = 50 credits = 0.50 at bet 0.01.
    public double winInCash;

    // Sent and deliberately unbound: "basePayout" (the paytable value for one way) and
    // "winInCredits" — winInCash already carries them, converted — and "winType", "WAYS_MATCH" on
    // every capture.
}

[Serializable]
public class ServerCell
{
    public int row;
    public int col;
}

#endregion

#region Client-Side Spin Request

[Serializable]
public class SpinRequest
{
    public string type = "SPIN";
    public SpinPayload payload;
}

[Serializable]
public class SpinPayload
{
    // betIndex is the only field with a confirmed effect. The server owns free-spin state, so
    // there is no isFreeSpin flag to send. The mock client also sends "spins": 100; nobody knows
    // what it does, and this client deliberately does not send it.
    public int betIndex;
}

#endregion

#region Game Configuration (Client Side Converted)

[Serializable]
public class GameConfig
{
    public int reelCount = 5;
    // Every row the server sends is live and pays.
    public int rowCount = 3;

    // The bet multiplier: total bet = selected bet x activeLine, from the init's creditDivisor (50).
    // The name is inherited from the payline games, where the two were the same number. Confirmed
    // against balances.
    public int activeLine = 50;

    // The number of ways (243), from the init's totalLines. What the line-count label shows.
    public int waysCount = 243;

    // Empty for this ways game: the init sends no paylines. Kept only while the payline win walk still
    // reads it.
    public List<List<int>> paylines;

    public List<double> availableBets;
    public List<SymbolInfo> symbols;

    // Resolved from the init symbol table. -1 means "not present", so an unresolved role can never
    // collide with a real symbol id the way a 0 default would — and -1 stays meaningful downstream:
    // SocketIOManager keeps both off the pre-spin placeholder board, and reads -1 as "no such
    // symbol, leave nothing out".
    public int wildSymbolId = -1;
    // The BlueOrb, from the symbol table's one scatter. SocketIOManager's placeholder opening board
    // reads it; feature code reads freeSpinsSymbolId, which features.scatter names and the converter
    // cross-checks.
    public int scatterSymbolId = -1;

    // Zero-based reels the Wild can land on, from features.wild. Empty when not sent.
    public List<int> wildMainGameReels = new List<int>();   // [1,2,3]
    public List<int> wildFreeGameReels = new List<int>();   // [1,2,3,4]

    // The free-games trigger symbol (the BlueOrb), from features.scatter.symbolId. -1 when not named.
    public int freeSpinsSymbolId = -1;
    public int freeSpinsMinTrigger;         // 3 BlueOrbs trigger

    // Free games, from features.freeGames. Zeros when not sent.
    public int freeSpinsCount;              // games per trigger: 10
    public int freeSpinsRetriggerCount;     // games per retrigger: 3
    public int freeSpinsMaxTotal;           // 300
    public double freeSpinsPayMultiplier;   // 1

    // The frames mechanic, from features.freeGames.frames. Rules unknown; nothing reads these yet.
    public double frameEndPrizeBlue;        // 2
    public double frameEndPrizePurple;      // 3
    public double frameEndPrizeOrange;      // 5
    public double frameOrangeHitMultiplier; // 10

    // Wild Jokers leftovers. Aladdin's Treasure has none of these features, so the init never sets
    // them and they stay -1 / empty, which keeps the Drum and Sandbell anticipation and the pick
    // bonuses switched off. Removed along with that code.
    public int ballBonusSymbolId = -1;
    public int sandbellBonusSymbolId = -1;
    public List<int> freeSpinsTriggerReels = new List<int>();
    public List<int> freeSpinsNearMissReels = new List<int>();
    public int freeSpinsRespinReel = -1;
}

[Serializable]
public class SymbolInfo
{
    public int id;
    public string name;
    // Descending from a full-reel match: index 0 = reelCount-of-a-kind, 1 = one fewer, and so on,
    // ending at minMatch — counts that don't pay are left off. In credits — multiply by the selected
    // (per-line) bet for cash.
    public List<double> multipliers;

    public bool isWild;
    public bool isScatter;
    // True for the Wild and the BlueOrb.
    public bool isSpecial;

    // Fewest matching reels that pay: 3 for every paying symbol. 0 when the symbol has no paytable
    // (the Wild and the BlueOrb).
    public int minMatch;
}

#endregion

#region Player & Game State (Client Side)

[Serializable]
public class PlayerData
{
    public double balance;
    public int currentBetIndex;
}

[Serializable]
public class SpinResult
{
    // Column-major: [reel][row], the transpose of the server's row-major matrix.
    public List<List<int>> resultMatrix;

    // Everything the spin paid, from grandTotalWin. This is the figure to show and the one the
    // balance moved by.
    public double winAmount;
    // payload.winAmount. It equals winAmount on every capture, so nothing reads it.
    public double lineWinAmount;

    // One entry per winning symbol (payload.waysWins). The name is inherited from the payline games.
    public List<WinLine> winLines;
    public PlayerData playerData;

    // The free-spins state, from payload.freeSpins. Never null.
    public FreeGameData freeGame;

    // The near-miss respin, or null when the spin had none. resultMatrix is the board after it.
    public RespinData respin;

    // The Sandbell Pick this spin won, or null.
    public SandbellPickData sandbellPick;

    // The Ball Gale Pick this spin won, or null.
    public BallGalePickData ballGalePick;
}

/// <summary>
/// A won Sandbell Pick (SandbellPickBonus.md §5, §8.2): which cells hold Sandbells and the values to
/// reveal. The winning value goes on the Sandbell the player clicks; the missed values on the rest.
/// </summary>
[Serializable]
public class SandbellPickData
{
    // Flat indices (row * reelCount + col) of every Sandbell, in board order: left to right, then top
    // to bottom within a reel.
    public List<int> cells;
    // The winning X value (× total bet).
    public double winValue;
    // The missed X values, in the order the server sent them — one per other Sandbell.
    public List<double> missedValues;
    // The prize in cash, the server's figure: TOTAL WIN.
    public double prize;
}

/// <summary>
/// A won Ball Gale Pick (BallGalePickBonus.md §6, §9.2): the picks in the order they are revealed —
/// the n-th click shows picks[n] — and the prize. Which gem shows which pick is the player's click.
/// </summary>
[Serializable]
public class BallGalePickData
{
    // In pickIndex order. The last has isEnd.
    public List<BallPick> picks;
    // The prize in cash, the server's figure: TOTAL WIN. Never the client's sum of the values.
    public double prize;
}

/// <summary>One revealed gem: an X value, or COLLECT.</summary>
[Serializable]
public class BallPick
{
    // The X value (× total bet). 0 on COLLECT.
    public double value;
    // Shows COLLECT instead of a value, and adds nothing to the total.
    public bool isCollect;
    // Picking ends after this one.
    public bool isEnd;
}

/// <summary>
/// A near-miss respin decided by the server: land originalMatrix, then respin reelIndex onto the
/// result's final board.
/// </summary>
[Serializable]
public class RespinData
{
    // Zero-based reel to respin.
    public int reelIndex;
    // Column-major like resultMatrix: the board the reels land on before the respin.
    public List<List<int>> originalMatrix;
    // The feature the near miss was for, as the server names it ("freeSpins", ...).
    public string featureName;
}

/// <summary>
/// One winning symbol — a ways win. The name is inherited from the payline games.
/// </summary>
[Serializable]
public class WinLine
{
    // Always -1: a ways win has no payline. Kept because the win walk still reads it (a -1 hides the
    // payline image).
    public int lineId;
    // The symbol that pays — never the Wild.
    public int symbolId;
    // Flat indices into the active grid: row * reelCount + col. Every paying cell, Wilds included,
    // possibly several on one reel. A cell can also be in another win's list.
    public List<int> positions;
    // The server's winInCash.
    public double winAmount;

    // Reels matched from the left: 3 to 5.
    public int matchCount;
    // The number of ways this symbol paid.
    public int waysCount;
    // Already inside winAmount. 1 when there is none.
    public double multiplier;
}

/// <summary>
/// The Free Spins facts for one spin, from payload.freeSpins (BonusFreeSpins.md §9.4).
/// A base-game trigger is spinsAwarded and not active; a retrigger is spinsAwarded and active.
/// </summary>
[Serializable]
public class FreeGameData
{
    // The block was sent. False on an ordinary base spin, and every other field is then zero.
    public bool present;

    // Spins were awarded on this spin — the trigger, or a retrigger inside the round.
    public bool spinsAwarded;

    // This spin was played inside the round. False on the base-game spin that triggers.
    public bool active;

    // Spins left AFTER this one. Retriggers are folded in, so it can go up as well as down.
    public int spinsRemaining;

    // Spins awarded in the round so far, retriggers included. The counter's Y; its X, the spins
    // played, is totalSpins - spinsRemaining.
    public int totalSpins;

    // The round's running total, server-authoritative, this spin included. Excludes the trigger
    // spin's own win.
    public double roundWin;
}

#endregion

#region Enums

public enum GameState
{
    Initializing,
    Idle,
    Spinning,
    Stopping,
    ShowingWin
}

public enum SpinSpeed
{
    Normal,
    Turbo,
    QuickSpin
}

public enum WinPopupType
{
    BigWin
}

#endregion

#region Helper Classes for Conversion

/// <summary>
/// The single seam between server JSON and client types. Wire shapes change here and nowhere else,
/// so the view and controller layers never see a backend revision.
/// </summary>
public static class InitDataConverter
{
    // Used only if the init carries no creditDivisor. Total bet = selected bet x 50, which matches
    // every captured balance.
    private const int FallbackBetMultiplier = 50;
    // Used only if the init carries no totalLines. Display only.
    private const int FallbackWaysCount = 243;

    internal static GameConfig ConvertToGameConfig(InitData serverData)
    {
        var gameData = serverData?.gameData;
        var serverSymbols = serverData?.uiData?.paylines?.symbols;
        var features = serverData?.features;

        int reelCount = (gameData?.lines != null && gameData.lines.Count > 0 && gameData.lines[0] != null)
            ? gameData.lines[0].Count
            : 5;

        var config = new GameConfig
        {
            reelCount = reelCount,
            rowCount = 3,
            activeLine = ResolveBetMultiplier(gameData),
            waysCount = ResolveWaysCount(gameData),
            paylines = ConvertPaylines(gameData?.lines, reelCount, 3),
            availableBets = gameData?.bets,
            symbols = new List<SymbolInfo>()
        };

        if (config.availableBets == null || config.availableBets.Count == 0)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] Init carried no bet levels — the bet controls will do nothing.");
        }

        if (serverSymbols == null)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] Init carried no symbol table — symbol art and paytables will be unresolved.");
            return config;
        }

        foreach (var serverSymbol in serverSymbols)
        {
            if (serverSymbol == null) continue;

            // The role comes from "group", never the name.
            string group = (serverSymbol.group ?? string.Empty).Trim().ToLowerInvariant();

            var multipliers = ConvertPaytable(serverSymbol, reelCount);

            var symbolInfo = new SymbolInfo
            {
                id = serverSymbol.id,
                name = serverSymbol.name,
                multipliers = multipliers,
                isWild = group == "wild",
                isScatter = group == "scatter",
                minMatch = multipliers.Count > 0 ? reelCount - multipliers.Count + 1 : 0
            };
            symbolInfo.isSpecial = symbolInfo.isWild || symbolInfo.isScatter;

            // The init sends minMatch too; a mismatch means the paytable and the rule disagree.
            if (multipliers.Count > 0 && serverSymbol.minMatch > 0 && serverSymbol.minMatch != symbolInfo.minMatch)
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] Symbol id {symbolInfo.id} (name '{symbolInfo.name}') sends minMatch {serverSymbol.minMatch} but its paytable starts at {symbolInfo.minMatch}-of-a-kind — using the paytable's.");
            }

            // An unknown group means the backend added a role this client has never heard of.
            if (group != "high" && group != "low" && !symbolInfo.isSpecial)
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] Symbol id {symbolInfo.id} (name '{symbolInfo.name}') has unrecognised group '{serverSymbol.group}' — it will be treated as an ordinary symbol.");
            }

            config.symbols.Add(symbolInfo);

            if (symbolInfo.isWild) config.wildSymbolId = symbolInfo.id;
            if (symbolInfo.isScatter) config.scatterSymbolId = symbolInfo.id;
        }

        // The Wild comes from its group above; features.wild.symbolId names it too, so the two are
        // only cross-checked.
        int? featureWildId = features?.wild?.symbolId;
        if (featureWildId.HasValue && featureWildId.Value != config.wildSymbolId)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] features.wild names symbol {featureWildId.Value} but the symbol table's wild is {config.wildSymbolId} — using the symbol table's.");
        }

        var wild = features?.wild;
        if (wild != null)
        {
            if (wild.mainGameReels != null) config.wildMainGameReels = new List<int>(wild.mainGameReels);
            if (wild.freeGameReels != null) config.wildFreeGameReels = new List<int>(wild.freeGameReels);
        }

        var scatter = features?.scatter;
        config.freeSpinsSymbolId = ResolveFeatureSymbolId(scatter?.symbolId, "scatter", config);
        if (config.freeSpinsSymbolId >= 0 && config.freeSpinsSymbolId != config.scatterSymbolId)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] features.scatter names symbol {config.freeSpinsSymbolId} but the symbol table's scatter is {config.scatterSymbolId} — free games use features.scatter's.");
        }
        config.freeSpinsMinTrigger = scatter != null ? scatter.minTrigger : 0;

        var freeGames = features?.freeGames;
        if (freeGames != null)
        {
            config.freeSpinsCount = freeGames.awardedOnTrigger;
            config.freeSpinsRetriggerCount = freeGames.awardedOnRetrigger;
            config.freeSpinsMaxTotal = freeGames.maxTotalFreeGames;
            config.freeSpinsPayMultiplier = freeGames.payMultiplier;

            var frames = freeGames.frames;
            if (frames != null)
            {
                config.frameOrangeHitMultiplier = frames.orangeHitMultiplier;
                if (frames.endPrizes != null)
                {
                    config.frameEndPrizeBlue = frames.endPrizes.blue;
                    config.frameEndPrizePurple = frames.endPrizes.purple;
                    config.frameEndPrizeOrange = frames.endPrizes.orange;
                }
            }
        }
        else
        {
            UnityEngine.Debug.LogError("[InitDataConverter] Init carried no features.freeGames — the free-games counts are unknown.");
        }

        return config;
    }

    // creditDivisor is the multiplier — confirmed against balances (a losing spin at bet 0.01 costs
    // exactly 0.50). totalLines (243) is the ways count, NOT the multiplier.
    private static int ResolveBetMultiplier(ServerGameData gameData)
    {
        if (gameData != null && gameData.creditDivisor > 0)
        {
            return gameData.creditDivisor;
        }

        UnityEngine.Debug.LogError($"[InitDataConverter] Init carried no creditDivisor — total bet assumed to be bet x {FallbackBetMultiplier}.");
        return FallbackBetMultiplier;
    }

    // totalLines is the ways count, shown on the line-count label. Display only.
    private static int ResolveWaysCount(ServerGameData gameData)
    {
        if (gameData != null && gameData.totalLines > 0)
        {
            return gameData.totalLines;
        }

        UnityEngine.Debug.LogError($"[InitDataConverter] Init carried no totalLines — the ways label assumes {FallbackWaysCount}.");
        return FallbackWaysCount;
    }

    // Empty for this ways game, which is expected and not logged. If lines are ever sent they are kept
    // as sent, and a malformed one is logged, not dropped: dropping it would shift every line after it.
    private static List<List<int>> ConvertPaylines(List<List<int>> lines, int reelCount, int rowCount)
    {
        var paylines = lines ?? new List<List<int>>();

        if (paylines.Count == 0)
        {
            return paylines;
        }

        for (int i = 0; i < paylines.Count; i++)
        {
            var line = paylines[i];
            if (line == null || line.Count != reelCount || line.Exists(row => row < 0 || row >= rowCount))
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] Payline {i} is not {reelCount} rows within 0..{rowCount - 1}: [{(line != null ? string.Join(",", line) : "null")}].");
            }
        }

        return paylines;
    }

    // The wire paytable ascends to a full-reel match; the client works descending from one, so it is
    // reversed. Any zero (a count that doesn't pay) is cut off the end, so the list stops at the
    // fewest that pay and nothing downstream shows a "pays 0" row.
    private static List<double> ConvertPaytable(ServerSymbolInfo serverSymbol, int reelCount)
    {
        var multipliers = new List<double>(serverSymbol.payout ?? new List<double>());

        if (multipliers.Count > reelCount)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] Symbol id {serverSymbol.id} sends {multipliers.Count} payouts for {reelCount} reels — the extra short counts are ignored.");
        }

        multipliers.Reverse();
        if (multipliers.Count > reelCount) multipliers.RemoveRange(reelCount, multipliers.Count - reelCount);
        while (multipliers.Count > 0 && multipliers[multipliers.Count - 1] <= 0) multipliers.RemoveAt(multipliers.Count - 1);

        return multipliers;
    }

    // A feature's symbol, checked against the symbol table. Missing or unknown reads as -1, never 0,
    // which is the Fairy.
    private static int ResolveFeatureSymbolId(int? symbolId, string featureName, GameConfig config)
    {
        if (!symbolId.HasValue)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] features.{featureName} names no symbolId — its trigger symbol is unknown.");
            return -1;
        }

        SymbolInfo symbol = config.symbols.Find(s => s.id == symbolId.Value);
        if (symbol == null)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] features.{featureName} names symbol {symbolId.Value}, which is not in the symbol table.");
            return -1;
        }

        if (!symbol.isScatter)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] features.{featureName} names symbol {symbolId.Value} ('{symbol.name}'), which is not a scatter.");
        }

        return symbolId.Value;
    }

    internal static PlayerData ConvertToPlayerData(ServerPlayer serverPlayer, int defaultBetIndex = 0)
    {
        return new PlayerData
        {
            balance = serverPlayer != null ? serverPlayer.balance : 0,
            currentBetIndex = defaultBetIndex
        };
    }

    /// <summary>
    /// Converts one spin response into the client's SpinResult.
    /// betAmount is accepted for signature stability but no longer used: the server always sends
    /// the post-spin balance, and the fallback below works off the already-deducted local balance.
    /// </summary>
    internal static SpinResult ConvertServerResponseToSpinResult(ServerSpinResponse serverResponse, double currentBalance, double betAmount, GameConfig gameConfig)
    {
        var payload = serverResponse?.payload;

        // grandTotalWin is the spin's total and passes straight through. payload.winAmount is kept
        // separately and never used as the figure to show. Nothing is summed here.
        double totalWin = payload?.grandTotalWin ?? 0;
        double newBalance = serverResponse?.player?.balance ?? CalculateNewBalance(currentBalance, totalWin);

        return new SpinResult
        {
            resultMatrix = ConvertMatrixToColumns(serverResponse?.matrix, gameConfig),
            winAmount = totalWin,
            lineWinAmount = payload?.winAmount ?? 0,
            winLines = ConvertWaysWins(payload?.waysWins, gameConfig),

            playerData = new PlayerData
            {
                balance = newBalance,
                currentBetIndex = 0
            },

            freeGame = ConvertFreeGame(payload?.freeSpins),
            respin = ConvertRespin(payload?.nearMissRespin, gameConfig),
            sandbellPick = ConvertSandbellPick(payload?.sandbellPickBonus, gameConfig),
            ballGalePick = ConvertBallGalePick(payload?.ballPickemBonus)
        };
    }

    // Null unless the bonus was won with at least one pick. The picks are put in pickIndex order; the
    // server's order already is, but nothing depends on that.
    private static BallGalePickData ConvertBallGalePick(ServerBallPickemBonus serverBonus)
    {
        if (serverBonus == null || !serverBonus.triggered) return null;

        var picks = new List<ServerBallPick>();
        if (serverBonus.picks != null)
        {
            foreach (ServerBallPick pick in serverBonus.picks)
            {
                if (pick != null) picks.Add(pick);
            }
        }

        if (picks.Count == 0)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] ballPickemBonus carried no picks — bonus ignored.");
            return null;
        }

        picks.Sort((a, b) => a.pickIndex.CompareTo(b.pickIndex));

        var converted = new List<BallPick>(picks.Count);
        foreach (ServerBallPick pick in picks)
        {
            converted.Add(new BallPick
            {
                value = pick.multiplier,
                isCollect = pick.isCollect,
                isEnd = pick.isEnd
            });
        }

        return new BallGalePickData
        {
            picks = converted,
            prize = serverBonus.winInCash
        };
    }

    // Null unless the bonus was won. The cells come from the server's positions (they match the
    // board); only the values matter beyond that — which Sandbell shows which is the click's.
    private static SandbellPickData ConvertSandbellPick(ServerSandbellPickBonus serverBonus, GameConfig gameConfig)
    {
        if (serverBonus == null || !serverBonus.triggered) return null;

        if (serverBonus.pickedPosition == null)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] sandbellPickBonus carried no pickedPosition — bonus ignored.");
            return null;
        }

        var cells = new List<int>();
        var missedValues = new List<double>();

        if (TryGetFlatIndex(serverBonus.pickedPosition.row, serverBonus.pickedPosition.col, gameConfig, "sandbellPickBonus.pickedPosition", out int pickedCell))
        {
            cells.Add(pickedCell);
        }

        if (serverBonus.unpickedPositions != null)
        {
            for (int i = 0; i < serverBonus.unpickedPositions.Count; i++)
            {
                ServerPickPosition position = serverBonus.unpickedPositions[i];
                if (position == null) continue;

                missedValues.Add(position.multiplier);
                if (TryGetFlatIndex(position.row, position.col, gameConfig, $"sandbellPickBonus.unpickedPositions[{i}]", out int cell))
                {
                    cells.Add(cell);
                }
            }
        }

        // Board order: by reel, then by row — the order the missed values are laid out in.
        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        cells.Sort((a, b) =>
        {
            int byReel = (a % reelCount).CompareTo(b % reelCount);
            return byReel != 0 ? byReel : (a / reelCount).CompareTo(b / reelCount);
        });

        return new SandbellPickData
        {
            cells = cells,
            winValue = serverBonus.pickedPosition.multiplier,
            missedValues = missedValues,
            prize = serverBonus.winInCash
        };
    }

    // Never returns null, so the controller can read it without guarding every access. A missing
    // block is an ordinary base spin: not in a round, nothing awarded.
    private static FreeGameData ConvertFreeGame(ServerFreeSpinsResult serverFreeSpins)
    {
        if (serverFreeSpins == null) return new FreeGameData();

        return new FreeGameData
        {
            present = true,
            spinsAwarded = serverFreeSpins.triggered,
            active = serverFreeSpins.active,
            spinsRemaining = serverFreeSpins.spinsRemaining,
            totalSpins = serverFreeSpins.totalSpins,
            roundWin = serverFreeSpins.totalWinCash
        };
    }

    // Null unless the server actually respun a reel. A respin without a usable board or reel is
    // logged and dropped: the client then just lands the final board, which is never wrong.
    private static RespinData ConvertRespin(ServerNearMissRespin serverRespin, GameConfig gameConfig)
    {
        if (serverRespin == null || !serverRespin.triggered) return null;

        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        if (serverRespin.reelIndex < 0 || serverRespin.reelIndex >= reelCount)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] nearMissRespin.reelIndex {serverRespin.reelIndex} is outside the {reelCount} reels — respin ignored.");
            return null;
        }

        if (serverRespin.originalMatrix == null || serverRespin.originalMatrix.Count == 0)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] nearMissRespin carried no originalMatrix — respin ignored.");
            return null;
        }

        return new RespinData
        {
            reelIndex = serverRespin.reelIndex,
            originalMatrix = ConvertMatrixToColumns(serverRespin.originalMatrix, gameConfig),
            featureName = serverRespin.featureName
        };
    }

    // The one place a row and column become a flat index, so the bounds check cannot drift between
    // callers.
    private static bool TryGetFlatIndex(int row, int col, GameConfig gameConfig, string source, out int flatIndex)
    {
        flatIndex = -1;

        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        int rowCount = gameConfig != null ? gameConfig.rowCount : 3;

        if (row < 0 || row >= rowCount || col < 0 || col >= reelCount)
        {
            UnityEngine.Debug.LogError($"[InitDataConverter] {source} is outside the {rowCount}x{reelCount} grid — cell skipped.");
            return false;
        }

        flatIndex = row * reelCount + col;
        return true;
    }

    // Server matrix is row-major (matrix[row][col]); the client works column-major ([reel][row]),
    // so this transposes. No padding rows to skip — every row the server sends is live.
    private static List<List<int>> ConvertMatrixToColumns(List<List<string>> serverMatrix, GameConfig gameConfig)
    {
        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        int rowCount = gameConfig != null ? gameConfig.rowCount : 3;

        if (serverMatrix == null || serverMatrix.Count == 0)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] Spin response carried no matrix.");
            return GenerateDefaultMatrix(reelCount, rowCount);
        }

        var matrix = new List<List<int>>();

        for (int col = 0; col < reelCount; col++)
        {
            var column = new List<int>();
            for (int row = 0; row < rowCount; row++)
            {
                if (row >= serverMatrix.Count || serverMatrix[row] == null || col >= serverMatrix[row].Count)
                {
                    UnityEngine.Debug.LogError($"[InitDataConverter] matrix has no cell at row {row}, col {col}.");
                    column.Add(0);
                    continue;
                }

                if (!int.TryParse(serverMatrix[row][col], out int symbolId))
                {
                    UnityEngine.Debug.LogError($"[InitDataConverter] Could not parse symbol at row {row}, col {col}.");
                    symbolId = 0;
                }

                column.Add(symbolId);
            }
            matrix.Add(column);
        }

        return matrix;
    }

    // One WinLine per winning symbol (a waysWins entry). The server already says which cells pay and
    // what the win is worth, so nothing is rebuilt or recomputed here.
    private static List<WinLine> ConvertWaysWins(List<ServerWaysWin> waysWins, GameConfig gameConfig)
    {
        var winLines = new List<WinLine>();
        if (waysWins == null) return winLines;

        for (int i = 0; i < waysWins.Count; i++)
        {
            var waysWin = waysWins[i];
            if (waysWin == null) continue;

            var flatPositions = new List<int>();
            if (waysWin.matchedPositions != null)
            {
                foreach (var cell in waysWin.matchedPositions)
                {
                    if (cell == null) continue;

                    if (TryGetFlatIndex(cell.row, cell.col, gameConfig, $"waysWins[{i}] cell ({cell.row},{cell.col})", out int flatIndex))
                    {
                        flatPositions.Add(flatIndex);
                    }
                }
            }

            winLines.Add(new WinLine
            {
                // A ways win has no payline. -1 keeps the walk's payline image hidden.
                lineId = -1,
                symbolId = waysWin.symbolId,
                positions = flatPositions,
                winAmount = waysWin.winInCash,
                matchCount = waysWin.matchCount,
                waysCount = waysWin.waysCount,
                // A missing field deserializes as 0, which would read as "pays nothing"; treat it
                // as no multiplier. Never clamps a real value: every win is x1 or more.
                multiplier = Math.Max(1.0, waysWin.appliedMultiplier)
            });
        }

        return winLines;
    }

    private static List<List<int>> GenerateDefaultMatrix(int reelCount, int rowCount)
    {
        var matrix = new List<List<int>>();
        for (int col = 0; col < reelCount; col++)
        {
            var column = new List<int>();
            for (int row = 0; row < rowCount; row++)
            {
                column.Add(0);
            }
            matrix.Add(column);
        }
        return matrix;
    }

    // currentBalance already reflects StartSpin()'s optimistic upfront deduction of the total bet,
    // so only the win is added back. Only reached if the server omits a balance.
    private static double CalculateNewBalance(double currentBalance, double winAmount)
    {
        return currentBalance + winAmount;
    }
}

#endregion
