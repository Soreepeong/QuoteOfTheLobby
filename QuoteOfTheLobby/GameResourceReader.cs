using Dalamud;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Data.Files;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuoteOfTheLobby
{
    public class GameResourceReader {
        private readonly IDataManager _dataManager;
        private readonly IClientState _clientState;

        public readonly List<Fdt> Fdts = new();
        public readonly List<byte[]> FontTextureData = new();

        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.World> _world;
        private readonly Dictionary<ClientLanguage, RandomQuoteReader> _randomQuoteReaders = new();

        public GameResourceReader(IDataManager dataManager, IClientState clientState) {
            _dataManager = dataManager;
            _clientState = clientState;

            _world = dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.World>()!;

            foreach (var fontName in Constants.FontNames)
                Fdts.Add(new Fdt(dataManager.GetFile($"common/font/{fontName}.fdt")!.Data));
            foreach (var i in Enumerable.Range(1, 100)) {
                var tf = dataManager.GameData.GetFile<TexFile>($"common/font/font{i}.tex");
                if (tf == null)
                    break;

                PluginLog.Debug($"Read common/font/font{i}.tex ({tf.Header.Width} x {tf.Header.Height})");
                if (tf.ImageData.Length != tf.Header.Width * tf.Header.Height * 4)
                    throw new Exception("Texture data error; corrupted game resource files?");

                FontTextureData.Add(tf.ImageData);
            }
        }

        public SeString? GetDatacenterNameFromWorldId(uint worldId) {
            return _world.GetRow(worldId)?.DataCenter.Value?.Name.ToDalamudString();
        }

        public SeString GetRandomQuote(ClientLanguage? language = null) {
            if (language == null)
                language = _clientState.ClientLanguage;

            if (!_randomQuoteReaders.ContainsKey((ClientLanguage)language))
                _randomQuoteReaders[(ClientLanguage)language] = new RandomQuoteReader(_dataManager, (ClientLanguage)language);

            return _randomQuoteReaders[(ClientLanguage)language].GetRandomQuote();
        }
    }
}
