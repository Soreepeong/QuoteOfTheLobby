using Dalamud.Data;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Utility;
using System;
using System.Linq;

namespace QuoteOfTheLobby {
    public class RandomQuoteReader {
        private static readonly string[] ValidDialogueSuffixes = { ".", "!", "?", "！", "？", "。", "…" };

        private readonly DataManager _dataManager;
        private readonly Random _random = new();

        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.InstanceContentTextData> _instanceContentTextData;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.PublicContentTextData> _publicContentTextData;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.PartyContentTextData> _partyContentTextData;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.NpcYell> _npcYell;

        public RandomQuoteReader(
            [RequiredVersion("1.0")] DataManager dataManager,
            Dalamud.ClientLanguage language) {
            _dataManager = dataManager;
            _instanceContentTextData = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.InstanceContentTextData>(language)!;
            _publicContentTextData = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.PublicContentTextData>(language)!;
            _partyContentTextData = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.PartyContentTextData>(language)!;
            _npcYell = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.NpcYell>(language)!;
        }

        public SeString GetRandomQuote() {
            var i = 0;
            while (i++ < 64) {
                SeString txt;
                var n = (uint)_random.Next((int)(_instanceContentTextData.RowCount + _publicContentTextData.RowCount + _partyContentTextData.RowCount + _npcYell.RowCount));
                try {
                    if (n < _instanceContentTextData.RowCount) {
                        txt = _instanceContentTextData.GetRow(n)!.Text.ToDalamudString();
                    } else {
                        n -= _instanceContentTextData.RowCount;
                        if (n < _publicContentTextData.RowCount) {
                            txt = _publicContentTextData.GetRow(n)!.TextData.ToDalamudString();
                        } else {
                            n -= _publicContentTextData.RowCount;
                            if (n < _partyContentTextData.RowCount) {
                                txt = _partyContentTextData.GetRow(n)!.Data.ToDalamudString();
                            } else {
                                n -= _partyContentTextData.RowCount;
                                txt = _npcYell.GetRow(n)!.Text.ToDalamudString();
                            }
                        }
                    }
                } catch (NullReferenceException) {
                    continue;
                }
                if (!ValidDialogueSuffixes.Any(x => txt.TextValue.EndsWith(x)))
                    continue;
                if (txt.Payloads.Any(x => x.Type != PayloadType.EmphasisItalic && x.Type != PayloadType.NewLine && x.Type != PayloadType.SeHyphen && x.Type != PayloadType.RawText))
                    continue;

                return txt;
            }
            return SeString.Empty;
        }
    }
}
