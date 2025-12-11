using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Flow.Launcher.Plugin.FreeDictionary
{
    public class QueryService : IDisposable
    {
        private readonly HttpClient httpClient = new();

        private const string iconPath = "icon.png";

        private Stream audioStream;

        private Mp3FileReader reader;

        private WaveOut waveOut;

        private const int lengthThreshold = 60;

        private const string Url = "https://api.dictionaryapi.dev/api/v2/entries/en/{0}";

        private string currentAudioUrl;

        public async Task<List<Result>> Query(string query, IPublicAPI publicAPI)
        {
            var url = string.Format(Url, query);

            var response = await httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new List<Result> { new() { Title = "No definitions found (｡•́︿•̀｡)", IcoPath = iconPath } };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new List<Result> { new() { Title = "An error occurred (｡•́︿•̀｡)", SubTitle = response.ReasonPhrase, IcoPath = iconPath } };
            }

            string content = await response.Content.ReadAsStringAsync();

            List<QueryResult> queryResult = System.Text.Json.JsonSerializer.Deserialize<List<QueryResult>>(content);

            QueryResult mainResult = queryResult[0];

            string phonetic = mainResult.Phonetic;
            string audioURL = null;
            for (int i = 0; i < mainResult.Phonetics.Length; i++)
            {
                if (string.IsNullOrEmpty(phonetic) && !string.IsNullOrEmpty(mainResult.Phonetics[i].Text))
                {
                    phonetic = mainResult.Phonetics[i].Text;
                }

                if (string.IsNullOrEmpty(audioURL) && !string.IsNullOrEmpty(mainResult.Phonetics[i].Audio))
                {
                    audioURL = mainResult.Phonetics[i].Audio;
                }

                if (!string.IsNullOrEmpty(phonetic) && !string.IsNullOrEmpty(audioURL))
                {
                    break;
                }
            }

            var results = new List<Result>();
            if (!string.IsNullOrEmpty(phonetic))
            {
                var result = new Result
                {
                    Title = phonetic,
                    SubTitle = "Phonetic " + (string.IsNullOrEmpty(audioURL) ? "(No audio)" : "(Select to play audio)"),
                    IcoPath = iconPath,
                    Action = (c) =>
                    {
                        PlayAudio(audioURL);
                        return false;
                    }
                };

                results.Add(result);
            }

            foreach (var meaning in mainResult.Meanings)
            {
                foreach (var definition in meaning.Definitions)
                {
                    var result = new Result
                    {
                        Title = definition.DefinitionText,
                        SubTitle = meaning.PartOfSpeech +
                            (definition.DefinitionText.Length > lengthThreshold ? " (Use preview (F1) to read full definition)" : string.Empty),
                        IcoPath = iconPath,
                        Action = (c) =>
                        {
                            publicAPI.CopyToClipboard(definition.DefinitionText);
                            return false;
                        }
                    };

                    results.Add(result);
                }
            }

            return results;
        }

        private async void PlayAudio(string audioURL)
        {
            if (string.IsNullOrEmpty(audioURL))
            {
                return;
            }
            if (audioURL != currentAudioUrl)
            {
                waveOut?.Stop();
                waveOut?.Dispose();
                await (reader?.DisposeAsync() ?? ValueTask.CompletedTask);
                await (audioStream?.DisposeAsync() ?? ValueTask.CompletedTask);

                byte[] audioBytes = await httpClient.GetByteArrayAsync(audioURL);

                audioStream = new MemoryStream(audioBytes);

                reader = new Mp3FileReader(audioStream);
                waveOut = new WaveOut();
                waveOut.Init(reader);

                currentAudioUrl = audioURL;
            }

            reader.Seek(0, SeekOrigin.Begin);
            waveOut.Play();
        }

        public void Dispose()
        {
            httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}