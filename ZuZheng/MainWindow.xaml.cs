using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MessageBox = HandyControl.Controls.MessageBox;

namespace ZuZheng
{
    /// <summary>
    ///     MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow
    {
        private static readonly string path = Directory.GetCurrentDirectory();
        private static readonly DirectoryInfo folder = new DirectoryInfo(path + @"\Android\");
        private static readonly DirectoryInfo Assets = new DirectoryInfo(path + @"\Android\Assets\");
        private static readonly DirectoryInfo AssetsMovie = new DirectoryInfo(path + @"\Android\Movie\");
        private static readonly DirectoryInfo AssetsAudio = new DirectoryInfo(path + @"\Android\Audio\");

        private static readonly string networkurlA =
            "https://line3-s2-bili-fate.bilibiligame.net/rongame_beta/rgfate/60_member/network/network_config_android_";

        private static readonly string networkurlB = ".json";

        private static readonly string member = "/rongame_beta/rgfate/60_member/member.php";
        private static readonly string developmentAuthCode = "aK8mTxBJCwZyxBjNJSKA5xCWL7zKtgZEQNiZmffXUbyQd5aLun";

        private static readonly string AssetStorageFilePath = folder.FullName + "AssetStorage_dec.txt";

        private static AskRequests request;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoadServerList(object sender, RoutedEventArgs e)
        {
            request = new AskRequests();
            request.appVerInput = InputAppVer.Text;
            var jsonFullUrl = networkurlA + request.appVerInput + networkurlB;
            string getServerList;
            try
            {
                getServerList = HttpRequest.Get(jsonFullUrl).ToText();
            }
            catch (Exception exception)
            {
                MessageBox.Error($"输入的版本号不合法,请重新输入!\r\n{exception}", "错误");
                InputAppVer.Text = "";
                return;
            }

            var getServerListJson = JObject.Parse(getServerList);
            request.serverVer = Convert.ToInt64(getServerListJson["list"][0]["version"][0].ToString().Replace("/", ""));
            try
            {
                request.serAddr = getServerListJson["list"][0]["androidSer"][0].ToString();
                request.cdnAddr = getServerListJson["list"][0]["cdn"][0].ToString();
            }
            catch (Exception exception)
            {
                MessageBox.Error($"不支持当前版本号,请重新输入!\r\n{exception}", "错误");
                InputAppVer.Text = "";
                return;
            }

            SelServer.Text = request.serAddr;
            Selcdn.Text = request.cdnAddr;
            var member_url = request.serAddr + member + $"?appVer={request.appVerInput}" +
                             $"&developmentAuthCode={developmentAuthCode}";
            var member_result = HttpRequest.Get(member_url).ToText();
            JObject decrypted_mr;
            try
            {
                decrypted_mr = JObject.Parse(Encoding.Default.GetString(Convert.FromBase64String(member_result)));
            }
            catch (Exception exception)
            {
                MessageBox.Error($"获取版本信息失败!\r\n{exception}", "错误");
                InputAppVer.Text = "";
                return;
            }

            if (!folder.Exists) folder.Create();
            request.asVer = decrypted_mr["response"][0]["success"]["assetStorageVersion"].ToString();
            request.mstVer = decrypted_mr["response"][0]["success"]["version"].ToString();
            if (request.asVer == "")
            {
                MessageBox.Error("获取版本信息失败!", "错误");
                InputAppVer.Text = "";
                return;
            }

            ASVersionDisplay.Text = request.asVer;
            mstVersionDisplay.Text = request.mstVer;
            //var mst_url = $"https://line3-patch-fate.bilibiligame.net/{request.serverVer}/MasterDataCachesOutput/{request.mstVer}/data.bin";
            //var mstData = HttpRequest.Get(mst_url).ToBinary();
            //File.WriteAllBytes(folder.FullName + "master", mstData);
            var assetstorage = HttpRequest
                .Get(request.cdnAddr + $"/NewResources/Android/AssetStorage.{request.asVer}.txt").ToText();
            File.WriteAllText(folder.FullName + "AssetStorage.txt", assetstorage);
            try
            {
                request.assetstorage_dec = CatAndMouseGame.MouseGame8(assetstorage);
                if (request.assetstorage_dec == null)
                {
                    MessageBox.Error("获取AssetStorage.txt失败!", "错误");
                    InputAppVer.Text = "";
                    ASStatus.Text = "未填充 ×";
                    return;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Error($"获取AssetStorage.txt失败!\r\n{exception}", "错误");
                InputAppVer.Text = "";
                ASStatus.Text = "未填充 ×";
                return;
            }

            File.WriteAllText(folder.FullName + "AssetStorage_dec.txt", request.assetstorage_dec);
            var assetStore = File.ReadAllLines(AssetStorageFilePath);
            var AudioArray = new JArray();
            var MovieArray = new JArray();
            var AssetArray = new JArray();
            for (var i = 2; i < assetStore.Length; ++i)
            {
                var tmp = assetStore[i].Split(',');
                string assetName;
                string fileName;
                if (tmp[4].Contains("Audio"))
                {
                    assetName = tmp[tmp.Length - 3].Replace('/', '@');
                    //fileName = CatAndMouseGame.GetMD5String(assetName);
                    fileName = tmp[0] + ".bin";
                    AudioArray.Add(new JObject(new JProperty("audioName", assetName),
                        new JProperty("fileName", fileName)));
                }
                else if (tmp[4].Contains("Movie"))
                {
                    assetName = tmp[tmp.Length - 3].Replace('/', '@');
                    //fileName = CatAndMouseGame.GetMD5String(assetName);
                    fileName = tmp[0] + ".bin";
                    MovieArray.Add(new JObject(new JProperty("movieName", assetName),
                        new JProperty("fileName", fileName)));
                }
                else if (!tmp[4].Contains("Movie"))
                {
                    assetName = tmp[tmp.Length - 3].Replace('/', '@') + ".unity3d";
                    //fileName = CatAndMouseGame.GetShaName(assetName);
                    fileName = tmp[0] + ".bin";
                    AssetArray.Add(new JObject(new JProperty("assetName", assetName),
                        new JProperty("fileName", fileName)));
                }
            }

            File.WriteAllText(folder.FullName + "AudioName.json", AudioArray.ToString());
            File.WriteAllText(folder.FullName + "MovieName.json", MovieArray.ToString());
            File.WriteAllText(folder.FullName + "AssetName.json", AssetArray.ToString());
            ASStatus.Text = "已填充并解密 √";
            DownloadMovieBtn.IsEnabled = true;
            DownloadAudioBtn.IsEnabled = true;
            DownloadAssetsBtn.IsEnabled = true;
        }

        private void DownloadMovieBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var DM = new Task(DownloadMovie);
            DownloadMovieBtn.IsEnabled = false;
            DownloadAudioBtn.IsEnabled = false;
            DownloadAssetsBtn.IsEnabled = false;
            DM.Start();
        }

        private void DownloadMovie()
        {
            var MovieList = (JArray)JsonConvert.DeserializeObject(File.ReadAllText(folder.FullName + "MovieName.json"));
            var ProgressValue = (double)10000 / MovieList.Count;
            Dispatcher.Invoke(() =>
            {
                progressbar.Value = 0;
                DownloadStatus.Items.Clear();
            });
            if (!AssetsMovie.Exists) AssetsMovie.Create();
            Parallel.ForEach(
                MovieList,
                new ParallelOptions { MaxDegreeOfParallelism = 2 },
                file =>
                {
                    var tmpfilenameshort = ((JObject)file)["fileName"].ToString().Substring(0, 2);
                    var tmpfilenamefull = ((JObject)file)["fileName"].ToString();
                    var filetruename = ((JObject)file)["movieName"].ToString();
                    var url = request.cdnAddr + $"/NewResources/Android/{tmpfilenameshort}/{tmpfilenamefull}";
                    byte[] Data;
                    try
                    {
                        Data = HttpRequest.Get(url).ToBinary();
                        Dispatcher.Invoke(() =>
                        {
                            DownloadStatus.Items.Insert(0, $"下载: {filetruename}");
                            progressbar.Value += ProgressValue;
                        });
                    }
                    catch (Exception)
                    {
                        Dispatcher.Invoke(() => { DownloadStatus.Items.Insert(0, $"重试: {filetruename}"); });
                        Thread.Sleep(5000);
                        try
                        {
                            Data = HttpRequest.Get(url).ToBinary();
                        }
                        catch (Exception e)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                DownloadStatus.Items.Insert(0, $"{e}");
                                DownloadStatus.Items.Insert(0, $"失败: {filetruename}");
                                progressbar.Value += ProgressValue;
                            });
                            return;
                        }
                    }

                    File.WriteAllBytes(AssetsMovie.FullName + $"{filetruename}", Data);
                }
            );
            Dispatcher.Invoke(() => { DownloadStatus.Items.Insert(0, "下载完成."); });
            Thread.Sleep(1500);
            Dispatcher.Invoke(() =>
            {
                DownloadStatus.Items.Clear();
                DownloadMovieBtn.IsEnabled = true;
                DownloadAudioBtn.IsEnabled = true;
                DownloadAssetsBtn.IsEnabled = true;
                progressbar.Value = 0;
            });
        }

        private void DownloadAudio()
        {
            var AudioList = (JArray)JsonConvert.DeserializeObject(File.ReadAllText(folder.FullName + "AudioName.json"));
            var ProgressValue = (double)10000 / AudioList.Count;
            Dispatcher.Invoke(() =>
            {
                progressbar.Value = 0;
                DownloadStatus.Items.Clear();
            });
            if (!AssetsAudio.Exists) AssetsAudio.Create();
            Parallel.ForEach(
                AudioList,
                new ParallelOptions { MaxDegreeOfParallelism = 2 },
                file =>
                {
                    var tmpfilenameshort = ((JObject)file)["fileName"].ToString().Substring(0, 2);
                    var tmpfilenamefull = ((JObject)file)["fileName"].ToString();
                    var filetruename = ((JObject)file)["audioName"].ToString();
                    var url = request.cdnAddr + $"/NewResources/Android/{tmpfilenameshort}/{tmpfilenamefull}";
                    byte[] Data;
                    try
                    {
                        Data = HttpRequest.Get(url).ToBinary();
                        Dispatcher.Invoke(() =>
                        {
                            DownloadStatus.Items.Insert(0, $"下载: {filetruename}");
                            progressbar.Value += ProgressValue;
                        });
                    }
                    catch (Exception)
                    {
                        Dispatcher.Invoke(() => { DownloadStatus.Items.Insert(0, $"重试: {filetruename}"); });
                        Thread.Sleep(5000);
                        try
                        {
                            Data = HttpRequest.Get(url).ToBinary();
                        }
                        catch (Exception e)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                DownloadStatus.Items.Insert(0, $"{e}");
                                DownloadStatus.Items.Insert(0, $"失败: {filetruename}");
                                progressbar.Value += ProgressValue;
                            });
                            return;
                        }
                    }

                    File.WriteAllBytes(AssetsAudio.FullName + $"{filetruename}", Data);
                }
            );
            Dispatcher.Invoke(() => { DownloadStatus.Items.Insert(0, "下载完成."); });
            Thread.Sleep(1500);
            Dispatcher.Invoke(() =>
            {
                DownloadStatus.Items.Clear();
                DownloadMovieBtn.IsEnabled = true;
                DownloadAudioBtn.IsEnabled = true;
                DownloadAssetsBtn.IsEnabled = true;
                progressbar.Value = 0;
            });
        }

        private void DownloadAudioBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var DA = new Task(DownloadAudio);
            DownloadMovieBtn.IsEnabled = false;
            DownloadAudioBtn.IsEnabled = false;
            DownloadAssetsBtn.IsEnabled = false;
            DA.Start();
        }

        private void DownloadAssetsBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var DAS = new Task(DownloadAssets);
            DownloadMovieBtn.IsEnabled = false;
            DownloadAudioBtn.IsEnabled = false;
            DownloadAssetsBtn.IsEnabled = false;
            DAS.Start();
        }

        private void DownloadAssets()
        {
            var AssetsList =
                (JArray)JsonConvert.DeserializeObject(File.ReadAllText(folder.FullName + "AssetName.json"));
            var ProgressValue = (double)10000 / AssetsList.Count;
            Dispatcher.Invoke(() =>
            {
                progressbar.Value = 0;
                DownloadStatus.Items.Clear();
            });
            if (!Assets.Exists) Assets.Create();
            Parallel.ForEach(
                AssetsList,
                new ParallelOptions { MaxDegreeOfParallelism = 2 },
                file =>
                {
                    var tmpfilenameshort = ((JObject)file)["fileName"].ToString().Substring(0, 2);
                    var tmpfilenamefull = ((JObject)file)["fileName"].ToString();
                    var filetruename = ((JObject)file)["assetName"].ToString();
                    var url = request.cdnAddr + $"/NewResources/Android/{tmpfilenameshort}/{tmpfilenamefull}";
                    byte[] Data;
                    try
                    {
                        Data = HttpRequest.Get(url).ToBinary();
                        Dispatcher.Invoke(() =>
                        {
                            DownloadStatus.Items.Insert(0, $"下载: {filetruename}");
                            progressbar.Value += ProgressValue;
                        });
                    }
                    catch (Exception)
                    {
                        Dispatcher.Invoke(() => { DownloadStatus.Items.Insert(0, $"重试: {filetruename}"); });
                        Thread.Sleep(5000);
                        try
                        {
                            Data = HttpRequest.Get(url).ToBinary();
                        }
                        catch (Exception e)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                DownloadStatus.Items.Insert(0, $"{e}");
                                DownloadStatus.Items.Insert(0, $"失败: {filetruename}");
                                progressbar.Value += ProgressValue;
                            });
                            return;
                        }
                    }

                    File.WriteAllBytes(Assets.FullName + $"{filetruename}", CatAndMouseGame.MouseGame4(Data));
                }
            );
            Dispatcher.Invoke(() => { DownloadStatus.Items.Insert(0, "下载完成."); });
            Thread.Sleep(1500);
            Dispatcher.Invoke(() =>
            {
                DownloadStatus.Items.Clear();
                DownloadMovieBtn.IsEnabled = true;
                DownloadAudioBtn.IsEnabled = true;
                DownloadAssetsBtn.IsEnabled = true;
                progressbar.Value = 0;
            });
        }

        private struct AskRequests
        {
            public string appVerInput;
            public long serverVer;
            public string serAddr;
            public string cdnAddr;
            public string asVer;
            public string mstVer;
            public string assetstorage_dec;
        }
    }
}