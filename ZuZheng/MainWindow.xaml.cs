using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Google.Protobuf;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.WindowsAPICodePack.Dialogs;
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
        private static readonly DirectoryInfo DifferMain = new DirectoryInfo(path + @"\Android\Differ\");

        private static readonly string networkurlA =
            "https://line3-s2-bili-fate.bilibiligame.net/rongame_beta/rgfate/60_member/network/network_config_android_";

        private static readonly string networkurlB = ".json";

        private static readonly string member = "/rongame_beta/rgfate/60_member/member.php";
        private static readonly string developmentAuthCode = "aK8mTxBJCwZyxBjNJSKA5xCWL7zKtgZEQNiZmffXUbyQd5aLun";

        private static readonly string AssetStorageFilePath = folder.FullName + "AssetStorage_dec.txt";

        private static AskRequests request;
        private static readonly object LockedList = new object();

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
            var member_result = HttpRequest.Get(member_url).ToText().Replace("%3D", "=");
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
            try
            {
                request.asVer = decrypted_mr["response"][0]["success"]["assetStorageVersion"].ToString();
                request.mstVer = decrypted_mr["response"][0]["success"]["version"].ToString();
            }
            catch (Exception exception)
            {
                try
                {
                    var failtitle = decrypted_mr["response"][0]["fail"]["title"].ToString();
                    var faildetail = decrypted_mr["response"][0]["fail"]["detail"].ToString().Replace(@"\n","\r\n");
                    MessageBox.Error($"获取信息失败!\r\n服务器返回:\r\n{failtitle}\r\n【公告内容】\r\n{faildetail}", "错误");
                    InputAppVer.Text = "";
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Error($"获取信息失败!未知错误。\r\n{ex}", "错误");
                    InputAppVer.Text = "";
                    return;
                }
            }
            if (request.asVer == "")
            {
                MessageBox.Error("获取版本信息失败!", "错误");
                InputAppVer.Text = "";
                return;
            }

            ASVersionDisplay.Text = request.asVer;
            mstVersionDisplay.Text = request.mstVer;
            var mst_url = request.cdnAddr + $"/MasterDataCachesOutput/{request.mstVer}/data.bin";
            var mstData = HttpRequest.Get(mst_url).ToBinary();
            if (!Directory.Exists(folder.FullName + @"\masterdata"))
                Directory.CreateDirectory(folder.FullName + @"\masterdata");
            File.WriteAllText(folder.FullName + @"\masterdata\member_request-raw.json", decrypted_mr.ToString());
            File.WriteAllBytes(folder.FullName + @"\masterdata\masterdata.bz2", mstData);
            var UMD = new Task(UnpackMasterData);
            try
            {
                UMD.Start();
            }
            catch (Exception exception)
            {
                MessageBox.Error("解析MasterData失败!\r\n\r\n错误: \r\n" + exception, "错误");
            }

            GC.Collect();
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

            TryAsWithDate.IsEnabled = true;
            StartChayiDownload.IsEnabled = true;
            File.WriteAllText(folder.FullName + "AssetStorage_dec.txt", request.assetstorage_dec);
            GC.Collect();
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
            GC.Collect();
        }

        private void UnpackMasterData()
        {
            Dispatcher.Invoke(() => { MainForm.Title += " - 正在写入MasterData数据文件..."; });
            var master = File.Open(folder.FullName + @"\masterdata\masterdata.bz2", FileMode.Open);
            var unpacker = new MemoryStream();
            BZip2.Decompress(master, unpacker);
            var masterdata = unpacker.ToArray();
            File.WriteAllBytes(folder.FullName + @"\masterdata\master_decompressed.protobuf", masterdata);
            master.Close();
            if (!Directory.Exists(folder.FullName + @"\masterdata\decrypted_masterdata"))
                Directory.CreateDirectory(folder.FullName + @"\masterdata\decrypted_masterdata");
            GC.Collect();
            using (var input = File.OpenRead(folder.FullName + @"\masterdata\master_decompressed.protobuf"))
            {
                var d_masterd = mst_data.Parser.ParseFrom(input);
                var formatter = new JsonFormatter(new JsonFormatter.Settings(true));

                var mstEvent = new EventEntityArray();
                mstEvent.MergeFrom(d_masterd.EventEntity);
                var mstEventJsonString = formatter.Format(mstEvent);
                var mstEventJObject = (JObject)JsonConvert.DeserializeObject(mstEventJsonString);
                var mstEventArray = (JArray)JsonConvert.DeserializeObject(mstEventJObject["eventEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstEvent.json",
                    mstEventArray.ToString());
                var mstEventMission = new EventMissionEntityArray();
                mstEventMission.MergeFrom(d_masterd.EventMissionEntity);
                var mstEventMissionJsonString = formatter.Format(mstEventMission);
                var mstEventMissionJObject = (JObject)JsonConvert.DeserializeObject(mstEventMissionJsonString);
                var mstEventMissionArray = (JArray)JsonConvert.DeserializeObject(mstEventMissionJObject["eventMissionEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstEventMission.json",
                    mstEventMissionArray.ToString());
                var mstAi = new AiEntityArray();
                mstAi.MergeFrom(d_masterd.AiEntity);
                var mstAiJsonString = formatter.Format(mstAi);
                var mstAiJObject = (JObject)JsonConvert.DeserializeObject(mstAiJsonString);
                var mstAiArray = (JArray)JsonConvert.DeserializeObject(mstAiJObject["aiEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstAi.json",
                    mstAiArray.ToString());
                var mstAiAct = new AiActEntityArray();
                mstAiAct.MergeFrom(d_masterd.AiActEntity);
                var mstAiActJsonString = formatter.Format(mstAiAct);
                var mstAiActJObject = (JObject)JsonConvert.DeserializeObject(mstAiActJsonString);
                var mstAiActArray = (JArray)JsonConvert.DeserializeObject(mstAiActJObject["aiActEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstAiAct.json",
                    mstAiActArray.ToString());
                var mstAiField = new AiFieldEntityArray();
                mstAiField.MergeFrom(d_masterd.AiFieldEntity);
                var mstAiFieldJsonString = formatter.Format(mstAiField);
                var mstAiFieldJObject = (JObject)JsonConvert.DeserializeObject(mstAiFieldJsonString);
                var mstAiFieldArray =
                    (JArray)JsonConvert.DeserializeObject(mstAiFieldJObject["aiFieldEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstAiField.json",
                    mstAiFieldArray.ToString());
                var mstStage = new StageEntityArray();
                mstStage.MergeFrom(d_masterd.StageEntity);
                var mstStageJsonString = formatter.Format(mstStage);
                var mstStageJObject = (JObject)JsonConvert.DeserializeObject(mstStageJsonString);
                var mstStageArray = (JArray)JsonConvert.DeserializeObject(mstStageJObject["stageEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstStage.json",
                    mstStageArray.ToString());
                GC.Collect();
                var mstGacha = new GachaEntityArray();
                mstGacha.MergeFrom(d_masterd.GachaEntity);
                var mstGachaJsonString = formatter.Format(mstGacha);
                var mstGachaJObject = (JObject)JsonConvert.DeserializeObject(mstGachaJsonString);
                var mstGachaArray = (JArray)JsonConvert.DeserializeObject(mstGachaJObject["gachaEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstGacha.json",
                    mstGachaArray.ToString());
                var mstGift = new GiftEntityArray();
                mstGift.MergeFrom(d_masterd.GiftEntity);
                var mstGiftJsonString = formatter.Format(mstGift);
                var mstGiftJObject = (JObject)JsonConvert.DeserializeObject(mstGiftJsonString);
                var mstGiftArray = (JArray)JsonConvert.DeserializeObject(mstGiftJObject["giftEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstGift.json",
                    mstGiftArray.ToString());
                var mstQuest = new QuestEntityArray();
                mstQuest.MergeFrom(d_masterd.QuestEntity);
                var mstQuestJsonString = formatter.Format(mstQuest);
                var mstQuestJObject = (JObject)JsonConvert.DeserializeObject(mstQuestJsonString);
                var mstQuestArray = (JArray)JsonConvert.DeserializeObject(mstQuestJObject["questEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstQuest.json",
                    mstQuestArray.ToString());
                var mstQuestPhase = new QuestPhaseEntityArray();
                mstQuestPhase.MergeFrom(d_masterd.QuestPhaseEntity);
                var mstQuestPhaseJsonString = formatter.Format(mstQuestPhase);
                var mstQuestPhaseJObject = (JObject)JsonConvert.DeserializeObject(mstQuestPhaseJsonString);
                var mstQuestPhaseArray =
                    (JArray)JsonConvert.DeserializeObject(mstQuestPhaseJObject["questPhaseEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstQuestPhase.json",
                    mstQuestPhaseArray.ToString());
                var mstQuestPhaseDetailAdd = new QuestPhaseDetailAddEntityArray();
                mstQuestPhaseDetailAdd.MergeFrom(d_masterd.QuestPhaseDetailAddEntity);
                var mstQuestPhaseDetailAddJsonString = formatter.Format(mstQuestPhaseDetailAdd);
                var mstQuestPhaseDetailAddJObject =
                    (JObject)JsonConvert.DeserializeObject(mstQuestPhaseDetailAddJsonString);
                var mstQuestPhaseDetailAddArray =
                    (JArray)JsonConvert.DeserializeObject(mstQuestPhaseDetailAddJObject["questPhaseDetailAddEntity"]
                        .ToString());
                File.WriteAllText(
                    folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstQuestPhaseDetailAdd.json",
                    mstQuestPhaseDetailAddArray.ToString());
                var mstQuestPickup = new QuestPickupEntityArray();
                mstQuestPickup.MergeFrom(d_masterd.QuestPickupEntity);
                var mstQuestPickupJsonString = formatter.Format(mstQuestPickup);
                var mstQuestPickupJObject = (JObject)JsonConvert.DeserializeObject(mstQuestPickupJsonString);
                var mstQuestPickupArray =
                    (JArray)JsonConvert.DeserializeObject(mstQuestPickupJObject["questPickupEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstQuestPickup.json",
                    mstQuestPickupArray.ToString());
                var mstItem = new ItemEntityArray();
                mstItem.MergeFrom(d_masterd.ItemEntity);
                var mstItemJsonString = formatter.Format(mstItem);
                var mstItemJObject = (JObject)JsonConvert.DeserializeObject(mstItemJsonString);
                var mstItemArray = (JArray)JsonConvert.DeserializeObject(mstItemJObject["itemEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstItem.json",
                    mstItemArray.ToString());
                var mstClassRelation = new ClassRelationEntityArray();
                mstClassRelation.MergeFrom(d_masterd.ClassRelationEntity);
                var mstClassRelationJsonString = formatter.Format(mstClassRelation);
                var mstClassRelationJObject = (JObject)JsonConvert.DeserializeObject(mstClassRelationJsonString);
                var mstClassRelationArray =
                    (JArray)JsonConvert.DeserializeObject(mstClassRelationJObject["classRelationEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstClassRelation.json",
                    mstClassRelationArray.ToString());
                var mstClass = new ServantClassEntityArray();
                mstClass.MergeFrom(d_masterd.ServantClassEntity);
                var mstClassJsonString = formatter.Format(mstClass);
                var mstClassJObject = (JObject)JsonConvert.DeserializeObject(mstClassJsonString);
                var mstClassArray =
                    (JArray)JsonConvert.DeserializeObject(mstClassJObject["servantClassEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstClass.json",
                    mstClassArray.ToString());
                var mstCampaignVoiceCaptions = new CampaignVoiceCaptionsEntityArray();
                mstCampaignVoiceCaptions.MergeFrom(d_masterd.CampaignVoiceCaptionsEntity);
                var mstCampaignVoiceCaptionsJsonString = formatter.Format(mstCampaignVoiceCaptions);
                var mstCampaignVoiceCaptionsJObject =
                    (JObject)JsonConvert.DeserializeObject(mstCampaignVoiceCaptionsJsonString);
                var mstCampaignVoiceCaptionsArray =
                    (JArray)JsonConvert.DeserializeObject(mstCampaignVoiceCaptionsJObject["campaignVoiceCaptionsEntity"]
                        .ToString());
                File.WriteAllText(
                    folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstCampaignVoiceCaptions.json",
                    mstCampaignVoiceCaptionsArray.ToString());
                var mstNpVoiceCaptions = new NpVoiceCaptionsEntityArray();
                mstNpVoiceCaptions.MergeFrom(d_masterd.NpVoiceCaptionsEntity);
                var mstNpVoiceCaptionsJsonString = formatter.Format(mstNpVoiceCaptions);
                var mstNpVoiceCaptionsJObject = (JObject)JsonConvert.DeserializeObject(mstNpVoiceCaptionsJsonString);
                var mstNpVoiceCaptionsArray =
                    (JArray)JsonConvert.DeserializeObject(mstNpVoiceCaptionsJObject["npVoiceCaptionsEntity"]
                        .ToString());
                foreach (var linevals in mstNpVoiceCaptionsArray)
                {
                    if (((JObject)linevals)["scriptJsonXuyaoshiyongjsonchulidebianliang"].ToString() == "") continue;

                    var needtoCvtJsonString =
                        ((JObject)linevals)["scriptJsonXuyaoshiyongjsonchulidebianliang"].ToString();
                    var CvtJson = (JArray)JsonConvert.DeserializeObject(needtoCvtJsonString);
                    ((JObject)linevals)["scriptJsonXuyaoshiyongjsonchulidebianliang"] =
                        (JArray)JsonConvert.DeserializeObject(CvtJson.ToString().Replace(" ", "").Replace("\r\n", "")
                            .Replace("[r]\n", ""));
                }

                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstNpVoiceCaptions.json",
                    mstNpVoiceCaptionsArray.ToString());
                GC.Collect();
                var mstBattleVoiceMsg = new BattleVoiceMsgEntityArray();
                mstBattleVoiceMsg.MergeFrom(d_masterd.BattleVoiceMsgEntity);
                var mstBattleVoiceMsgJsonString = formatter.Format(mstBattleVoiceMsg);
                var mstBattleVoiceMsgJObject = (JObject)JsonConvert.DeserializeObject(mstBattleVoiceMsgJsonString);
                var mstBattleVoiceMsgArray =
                    (JArray)JsonConvert.DeserializeObject(mstBattleVoiceMsgJObject["battleVoiceMsgEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstBattleVoiceMsg.json",
                    mstBattleVoiceMsgArray.ToString());
                var mstCombineLimit = new CombineLimitEntityArray();
                mstCombineLimit.MergeFrom(d_masterd.CombineLimitEntity);
                var mstCombineLimitJsonString = formatter.Format(mstCombineLimit);
                var mstCombineLimitJObject = (JObject)JsonConvert.DeserializeObject(mstCombineLimitJsonString);
                var mstCombineLimitArray =
                    (JArray)JsonConvert.DeserializeObject(mstCombineLimitJObject["combineLimitEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstCombineLimit.json",
                    mstCombineLimitArray.ToString());
                var mstCombineSkill = new CombineSkillEntityArray();
                mstCombineSkill.MergeFrom(d_masterd.CombineSkillEntity);
                var mstCombineSkillJsonString = formatter.Format(mstCombineSkill);
                var mstCombineSkillJObject = (JObject)JsonConvert.DeserializeObject(mstCombineSkillJsonString);
                var mstCombineSkillArray =
                    (JArray)JsonConvert.DeserializeObject(mstCombineSkillJObject["combineSkillEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstCombineSkill.json",
                    mstCombineSkillArray.ToString());
                var mstSvt = new ServantEntityArray();
                mstSvt.MergeFrom(d_masterd.ServantEntity);
                var mstSvtJsonString = formatter.Format(mstSvt);
                var mstSvtJObject = (JObject)JsonConvert.DeserializeObject(mstSvtJsonString);
                var mstSvtArray = (JArray)JsonConvert.DeserializeObject(mstSvtJObject["servantEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvt.json",
                    mstSvtArray.ToString());
                var mstSvtVoice = new ServantVoiceEntityArray();
                mstSvtVoice.MergeFrom(d_masterd.ServantVoiceEntity);
                var mstSvtVoiceJsonString = formatter.Format(mstSvtVoice);
                var mstSvtVoiceJObject = (JObject)JsonConvert.DeserializeObject(mstSvtVoiceJsonString);
                var mstSvtVoiceArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtVoiceJObject["servantVoiceEntity"].ToString());
                foreach (var linevals in mstSvtVoiceArray)
                {
                    if (((JObject)linevals)["scriptJsonXuyaoshiyongjsonchulidebianliang"].ToString() == "") continue;

                    var needtoCvtJsonString =
                        ((JObject)linevals)["scriptJsonXuyaoshiyongjsonchulidebianliang"].ToString();
                    var CvtJson = (JArray)JsonConvert.DeserializeObject(needtoCvtJsonString);
                    ((JObject)linevals)["scriptJsonXuyaoshiyongjsonchulidebianliang"] =
                        (JArray)JsonConvert.DeserializeObject(CvtJson.ToString().Replace(" ", "").Replace("\r\n", "")
                            .Replace("[r]\n", ""));
                }

                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtVoice.json",
                    mstSvtVoiceArray.ToString());
                GC.Collect();
                var mstSvtSkill = new ServantSkillEntityArray();
                mstSvtSkill.MergeFrom(d_masterd.ServantSkillEntity);
                var mstSvtSkillJsonString = formatter.Format(mstSvtSkill);
                var mstSvtSkillJObject = (JObject)JsonConvert.DeserializeObject(mstSvtSkillJsonString);
                var mstSvtSkillArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtSkillJObject["servantSkillEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtSkill.json",
                    mstSvtSkillArray.ToString());
                var mstSvtSkillRelease = new ServantSkillReleaseEntityArray();
                mstSvtSkillRelease.MergeFrom(d_masterd.ServantSkillReleaseEntity);
                var mstSvtSkillReleaseJsonString = formatter.Format(mstSvtSkillRelease);
                var mstSvtSkillReleaseJObject = (JObject)JsonConvert.DeserializeObject(mstSvtSkillReleaseJsonString);
                var mstSvtSkillReleaseArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtSkillReleaseJObject["servantSkillReleaseEntity"]
                        .ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtSkillRelease.json",
                    mstSvtSkillReleaseArray.ToString());
                var mstSvtFilter = new ServantFilterEntityArray();
                mstSvtFilter.MergeFrom(d_masterd.ServantFilterEntity);
                var mstSvtFilterJsonString = formatter.Format(mstSvtFilter);
                var mstSvtFilterJObject = (JObject)JsonConvert.DeserializeObject(mstSvtFilterJsonString);
                var mstSvtFilterArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtFilterJObject["servantFilterEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtFilter.json",
                    mstSvtFilterArray.ToString());
                var mstSvtLimitAdd = new ServantLimitAddEntityArray();
                mstSvtLimitAdd.MergeFrom(d_masterd.ServantLimitAddEntity);
                var mstSvtLimitAddJsonString = formatter.Format(mstSvtLimitAdd);
                var mstSvtLimitAddJObject = (JObject)JsonConvert.DeserializeObject(mstSvtLimitAddJsonString);
                var mstSvtLimitAddArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtLimitAddJObject["servantLimitAddEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtLimitAdd.json",
                    mstSvtLimitAddArray.ToString());
                var mstSvtInviduality = new ServantIndividualityEntityArray();
                mstSvtInviduality.MergeFrom(d_masterd.ServantIndividualityEntity);
                var mstSvtInvidualityJsonString = formatter.Format(mstSvtInviduality);
                var mstSvtInvidualityJObject = (JObject)JsonConvert.DeserializeObject(mstSvtInvidualityJsonString);
                var mstSvtInvidualityArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtInvidualityJObject["servantIndividualityEntity"]
                        .ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtInviduality.json",
                    mstSvtInvidualityArray.ToString());
                var mstSvtPassiveSkill = new ServantPassiveSkillEntityArray();
                mstSvtPassiveSkill.MergeFrom(d_masterd.ServantPassiveSkillEntity);
                var mstSvtPassiveSkillJsonString = formatter.Format(mstSvtPassiveSkill);
                var mstSvtPassiveSkillJObject = (JObject)JsonConvert.DeserializeObject(mstSvtPassiveSkillJsonString);
                var mstSvtPassiveSkillArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtPassiveSkillJObject["servantPassiveSkillEntity"]
                        .ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtPassiveSkill.json",
                    mstSvtPassiveSkillArray.ToString());
                var mstSvtAppendPassiveSkill = new ServantAppendPassiveSkillEntityArray();
                mstSvtAppendPassiveSkill.MergeFrom(d_masterd.ServantAppendPassiveSkillEntity);
                var mstSvtAppendPassiveSkillJsonString = formatter.Format(mstSvtAppendPassiveSkill);
                var mstSvtAppendPassiveSkillJObject =
                    (JObject)JsonConvert.DeserializeObject(mstSvtAppendPassiveSkillJsonString);
                if (!d_masterd.ServantAppendPassiveSkillEntity.IsEmpty)
                {
                    var mstSvtAppendPassiveSkillArray =
                        (JArray)JsonConvert.DeserializeObject(
                            mstSvtAppendPassiveSkillJObject["servantAppendPassiveSkillEntity"].ToString());
                    File.WriteAllText(
                        folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtAppendPassiveSkill.json",
                        mstSvtAppendPassiveSkillArray.ToString());
                }
                else
                {
                    File.WriteAllText(
                        folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtAppendPassiveSkill.json", "[]");
                }

                GC.Collect();
                var mstSvtExp = new ServantExpEntityArray();
                mstSvtExp.MergeFrom(d_masterd.ServantExpEntity);
                var mstSvtExpJsonString = formatter.Format(mstSvtExp);
                var mstSvtExpJObject = (JObject)JsonConvert.DeserializeObject(mstSvtExpJsonString);
                var mstSvtExpArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtExpJObject["servantExpEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtExp.json",
                    mstSvtExpArray.ToString());
                var mstSvtCostume = new ServantCostumeEntityArray();
                mstSvtCostume.MergeFrom(d_masterd.ServantCostumeEntity);
                var mstSvtCostumeJsonString = formatter.Format(mstSvtCostume);
                var mstSvtCostumeJObject = (JObject)JsonConvert.DeserializeObject(mstSvtCostumeJsonString);
                var mstSvtCostumeArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtCostumeJObject["servantCostumeEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtCostume.json",
                    mstSvtCostumeArray.ToString());
                var mstSvtLimit = new ServantLimitEntityArray();
                mstSvtLimit.MergeFrom(d_masterd.ServantLimitEntity);
                var mstSvtLimitJsonString = formatter.Format(mstSvtLimit);
                var mstSvtLimitJObject = (JObject)JsonConvert.DeserializeObject(mstSvtLimitJsonString);
                var mstSvtLimitArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtLimitJObject["servantLimitEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtLimit.json",
                    mstSvtLimitArray.ToString());
                var mstSvtCard = new ServantCardEntityArray();
                mstSvtCard.MergeFrom(d_masterd.ServantCardEntity);
                var mstSvtCardJsonString = formatter.Format(mstSvtCard);
                var mstSvtCardJObject = (JObject)JsonConvert.DeserializeObject(mstSvtCardJsonString);
                var mstSvtCardArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtCardJObject["servantCardEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtCard.json",
                    mstSvtCardArray.ToString());
                GC.Collect();
                var mstSvtComment = new ServantCommentEntityArray();
                mstSvtComment.MergeFrom(d_masterd.ServantCommentEntity);
                var mstSvtCommentJsonString = formatter.Format(mstSvtComment);
                var mstSvtCommentJObject = (JObject)JsonConvert.DeserializeObject(mstSvtCommentJsonString);
                var mstSvtCommentArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtCommentJObject["servantCommentEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtComment.json",
                    mstSvtCommentArray.ToString());
                var mstSvtTreasureDevice = new ServantTreasureDvcEntityArray();
                mstSvtTreasureDevice.MergeFrom(d_masterd.ServantTreasureDvcEntity);
                var mstSvtTreasureDeviceJsonString = formatter.Format(mstSvtTreasureDevice);
                var mstSvtTreasureDeviceJObject =
                    (JObject)JsonConvert.DeserializeObject(mstSvtTreasureDeviceJsonString);
                var mstSvtTreasureDeviceArray =
                    (JArray)JsonConvert.DeserializeObject(mstSvtTreasureDeviceJObject["servantTreasureDvcEntity"]
                        .ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSvtTreasureDevice.json",
                    mstSvtTreasureDeviceArray.ToString());
                var mstBuff = new BuffEntityArray();
                mstBuff.MergeFrom(d_masterd.BuffEntity);
                var mstBuffJsonString = formatter.Format(mstBuff);
                var mstBuffJObject = (JObject)JsonConvert.DeserializeObject(mstBuffJsonString);
                var mstBuffArray = (JArray)JsonConvert.DeserializeObject(mstBuffJObject["buffEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstBuff.json",
                    mstBuffArray.ToString());
                GC.Collect();
                var mstCv = new CvEntityArray();
                mstCv.MergeFrom(d_masterd.CvEntity);
                var mstCvJsonString = formatter.Format(mstCv);
                var mstCvJObject = (JObject)JsonConvert.DeserializeObject(mstCvJsonString);
                var mstCvArray = (JArray)JsonConvert.DeserializeObject(mstCvJObject["cvEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstCv.json",
                    mstCvArray.ToString());
                var mstIllustrator = new IllustratorEntityArray();
                mstIllustrator.MergeFrom(d_masterd.IllustratorEntity);
                var mstIllustratorJsonString = formatter.Format(mstIllustrator);
                var mstIllustratorJObject = (JObject)JsonConvert.DeserializeObject(mstIllustratorJsonString);
                var mstIllustratorArray =
                    (JArray)JsonConvert.DeserializeObject(mstIllustratorJObject["illustratorEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstIllustrator.json",
                    mstIllustratorArray.ToString());
                var mstFunc = new FunctionEntityArray();
                mstFunc.MergeFrom(d_masterd.FunctionEntity);
                var mstFuncJsonString = formatter.Format(mstFunc);
                var mstFuncJObject = (JObject)JsonConvert.DeserializeObject(mstFuncJsonString);
                var mstFuncArray = (JArray)JsonConvert.DeserializeObject(mstFuncJObject["functionEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstFunc.json",
                    mstFuncArray.ToString());
                var mstSkill = new SkillEntityArray();
                mstSkill.MergeFrom(d_masterd.SkillEntity);
                var mstSkillJsonString = formatter.Format(mstSkill);
                var mstSkillJObject = (JObject)JsonConvert.DeserializeObject(mstSkillJsonString);
                var mstSkillArray = (JArray)JsonConvert.DeserializeObject(mstSkillJObject["skillEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSkill.json",
                    mstSkillArray.ToString());
                GC.Collect();
                var mstSkillDetail = new SkillDetailEntityArray();
                mstSkillDetail.MergeFrom(d_masterd.SkillDetailEntity);
                var mstSkillDetailJsonString = formatter.Format(mstSkillDetail);
                var mstSkillDetailJObject = (JObject)JsonConvert.DeserializeObject(mstSkillDetailJsonString);
                var mstSkillDetailArray =
                    (JArray)JsonConvert.DeserializeObject(mstSkillDetailJObject["skillDetailEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSkillDetail.json",
                    mstSkillDetailArray.ToString());
                var mstSkillLv = new SkillLvEntityArray();
                mstSkillLv.MergeFrom(d_masterd.SkillLvEntity);
                var mstSkillLvJsonString = formatter.Format(mstSkillLv);
                var mstSkillLvJObject = (JObject)JsonConvert.DeserializeObject(mstSkillLvJsonString);
                var mstSkillLvArray = (JArray)JsonConvert.DeserializeObject(mstSkillLvJObject["skillLvEntity"]
                    .ToString().Replace("\"vals\"", "\"svals\""));
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstSkillLv.json",
                    mstSkillLvArray.ToString());
                var mstTreasureDevice = new TreasureDvcEntityArray();
                mstTreasureDevice.MergeFrom(d_masterd.TreasureDvcEntity);
                var mstTreasureDeviceJsonString = formatter.Format(mstTreasureDevice);
                var mstTreasureDeviceJObject = (JObject)JsonConvert.DeserializeObject(mstTreasureDeviceJsonString);
                var mstTreasureDeviceArray =
                    (JArray)JsonConvert.DeserializeObject(mstTreasureDeviceJObject["treasureDvcEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstTreasureDevice.json",
                    mstTreasureDeviceArray.ToString());
                var mstTreasureDeviceDetail = new TreasureDvcDetailEntityArray();
                mstTreasureDeviceDetail.MergeFrom(d_masterd.TreasureDvcDetailEntity);
                var mstTreasureDeviceDetailJsonString = formatter.Format(mstTreasureDeviceDetail);
                var mstTreasureDeviceDetailJObject =
                    (JObject)JsonConvert.DeserializeObject(mstTreasureDeviceDetailJsonString);
                var mstTreasureDeviceDetailArray =
                    (JArray)JsonConvert.DeserializeObject(mstTreasureDeviceDetailJObject["treasureDvcDetailEntity"]
                        .ToString());
                File.WriteAllText(
                    folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstTreasureDeviceDetail.json",
                    mstTreasureDeviceDetailArray.ToString());
                GC.Collect();
                var mstTreasureDeviceLv = new TreasureDvcLvEntityArray();
                mstTreasureDeviceLv.MergeFrom(d_masterd.TreasureDvcLvEntity);
                var mstTreasureDeviceLvJsonString = formatter.Format(mstTreasureDeviceLv);
                var mstTreasureDeviceLvJObject = (JObject)JsonConvert.DeserializeObject(mstTreasureDeviceLvJsonString);
                var mstTreasureDeviceLvArray =
                    (JArray)JsonConvert.DeserializeObject(mstTreasureDeviceLvJObject["treasureDvcLvEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstTreasureDeviceLv.json",
                    mstTreasureDeviceLvArray.ToString().Replace("\"vals", "\"svals"));
                var npcSvtFollower = new NpcServantFollowerEntityArray();
                npcSvtFollower.MergeFrom(d_masterd.NpcServantFollowerEntity);
                var npcSvtFollowerJsonString = formatter.Format(npcSvtFollower);
                var npcSvtFollowerJObject = (JObject)JsonConvert.DeserializeObject(npcSvtFollowerJsonString);
                var npcSvtFollowerArray =
                    (JArray)JsonConvert.DeserializeObject(npcSvtFollowerJObject["npcServantFollowerEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "npcSvtFollower.json",
                    npcSvtFollowerArray.ToString());
                var mstMovieTalk = new MovieTalkEntityArray();
                mstMovieTalk.MergeFrom(d_masterd.MovieTalkEntity);
                var mstMovieTalkJsonString = formatter.Format(mstMovieTalk);
                var mstMovieTalkJObject = (JObject)JsonConvert.DeserializeObject(mstMovieTalkJsonString);
                var mstMovieTalkArray =
                    (JArray)JsonConvert.DeserializeObject(mstMovieTalkJObject["movieTalkEntity"].ToString());
                File.WriteAllText(folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstMovieTalk.json",
                    mstMovieTalkArray.ToString());
                var mstAssetbundleKey = new AssetbundleKeyEntityArray();
                mstAssetbundleKey.MergeFrom(d_masterd.AssetbundleKeyEntity);
                var mstAssetbundleKeyJsonString = formatter.Format(mstAssetbundleKey);
                var mstAssetbundleKeyJObject = (JObject)JsonConvert.DeserializeObject(mstAssetbundleKeyJsonString);
                if (!d_masterd.ServantAppendPassiveSkillEntity.IsEmpty)
                {
                    var mstAssetbundleKeyArray =
                        (JArray)JsonConvert.DeserializeObject(mstAssetbundleKeyJObject["assetbundleKeyEntity"].ToString());
                    File.WriteAllText(
                        folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstAssetbundleKey.json",
                        mstAssetbundleKeyArray.ToString());
                }
                else
                {
                    File.WriteAllText(
                        folder.FullName + @"\masterdata\decrypted_masterdata\" + "mstAssetbundleKey.json", "[]");
                }
                MessageBox.Info("MasterData数据写入完成.", "写入完成");
                Dispatcher.Invoke(() => { MainForm.Title = "ZuZheng"; });
            }

            GC.Collect();
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

        private void TryAssetStorages(object sender, RoutedEventArgs e)
        {
            var date = InputTryDate.Text;
            TryAsWithDate.IsEnabled = false;
            if (date.Length != 8)
            {
                MessageBox.Error("日期格式错误(例: 20220612).", "错误");
                TryAsWithDate.IsEnabled = true;
                InputTryDate.Text = "";
                return;
            }

            Task.Run(() => TAS(date));
        }

        private void TAS(string date_ins)
        {
            Dispatcher.Invoke(() =>
            {
                TryAsStatus.Items.Clear();
                astrybar.Value = 0.0;
            });
            var p_value = (double)10000 / 2400;
            for (var i = 0; i < 2400; i++)
            {
                var Time = i.ToString("0000");
                if (Convert.ToInt32(Time.Substring(2, 2)) >= 60)
                {
                    Dispatcher.Invoke(() => { astrybar.Value += p_value; });
                    continue;
                }

                var str =
                    $"{request.cdnAddr}/NewResources/Android/AssetStorage.{date_ins}{Time}.txt";
                try
                {
                    var assetstorage = HttpRequest.Get(str).ToText();
                    Dispatcher.Invoke(() =>
                    {
                        TryAsStatus.Items.Insert(0, $"尝试: AssetStorage.{date_ins}{Time}.txt - 状态: Success.");
                        TryAsStatus.Items.Insert(0, "写入AssetStorage文件...");
                        astrybar.Value += p_value;
                    });
                    File.WriteAllText(folder.FullName + $"AssetStorage.{date_ins}{Time}.txt", assetstorage);
                    Dispatcher.Invoke(() => { TryAsStatus.Items.Insert(0, "解密AssetStorage文件..."); });
                    var as_read = File.ReadAllText(folder.FullName + $"AssetStorage.{date_ins}{Time}.txt");
                    var as_output = CatAndMouseGame.MouseGame8(as_read);
                    File.WriteAllText(folder.FullName + $"AssetStorage_dec.{date_ins}{Time}.txt", as_output);
                    Dispatcher.Invoke(() => { TryAsStatus.Items.Insert(0, "获取成功."); });
                    Dispatcher.Invoke(() => { astrybar.Value = astrybar.Maximum; });
                    break;
                }
                catch (Exception)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TryAsStatus.Items.Insert(0, $"尝试: AssetStorage.{date_ins}{Time}.txt - 状态: 404 Not Found.");
                        astrybar.Value += p_value;
                    });
                    if (i == 2359)
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Error("未能按照当前指定的日期获取到AssetStorage文件.\r\n请尝试其他日期!", "失败");
                            TryAsStatus.Items.Insert(0, "获取失败.请尝试其他日期.");
                            astrybar.Value = astrybar.Maximum;
                        });
                }
            }

            Thread.Sleep(2000);
            Dispatcher.Invoke(() =>
            {
                TryAsWithDate.IsEnabled = true;
                TryAsStatus.Items.Clear();
                astrybar.Value = 0.0;
            });
            GC.Collect();
        }

        private void StartChayiDownload_OnClick(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(DifferMain.FullName)) Directory.CreateDirectory(DifferMain.FullName);
            AsDownloadStatus.Items.Clear();
            Asprogressbar2.Value = 0.0;
            var inputolddialog = new CommonOpenFileDialog { Title = "选择旧版本的AssetStorage文件.", Multiselect = false };
            inputolddialog.Filters.Add(new CommonFileDialogFilter("已解密的AssetStorage文件", "txt"));
            var resultoldinput = inputolddialog.ShowDialog();
            var input_old = "";
            if (resultoldinput == CommonFileDialogResult.Ok) input_old = inputolddialog.FileName;
            if (input_old == "")
                return;
            if (!IsDecryptedAssetStorage(File.ReadAllText(input_old)))
            {
                MessageBox.Error("该文件不是解密后的AssetStorage.txt文件", "错误");
                return;
            }

            GC.Collect();
            var inputnewdialog = new CommonOpenFileDialog { Title = "选择新版本的AssetStorage文件.", Multiselect = false };
            inputnewdialog.Filters.Add(new CommonFileDialogFilter("已解密的AssetStorage文件", "txt"));
            var resultnewinput = inputnewdialog.ShowDialog();
            var input_new = "";
            if (resultnewinput == CommonFileDialogResult.Ok) input_new = inputnewdialog.FileName;
            if (input_new == "")
                return;
            if (!IsDecryptedAssetStorage(File.ReadAllText(input_new)))
            {
                MessageBox.Error("该文件不是解密后的AssetStorage.txt文件", "错误");
                return;
            }

            GC.Collect();
            ResetASBtn.IsEnabled = true;
            StartChayiDownload.IsEnabled = false;
            MessageBox.Info("请在下方进度条不再滚动之后点击\"重置\"按钮重置状态.", "提示");
            Task.Run(() => ASDiffer(input_old, input_new));
        }

        private bool IsDecryptedAssetStorage(string input)
        {
            return input.Contains("@FgoDataVersion");
        }

        private void ASDiffer(string old_dir, string new_dir)
        {
            var ASLine = File.ReadAllLines(new_dir);
            var ASOldLine = File.ReadAllLines(old_dir);
            var tmpold = new string[ASOldLine.Length, 5];
            var tmp = new string[ASLine.Length, 5];
            var launched_date = DateTime.Now.ToString("yyyyMMddhhmmss");
            var dest = DifferMain.FullName + $@"\Differ_{launched_date}\";
            var p_val = (double)100000 / ASLine.Length;
            for (var kk = 0; kk < ASLine.Length; kk++)
            {
                var tmpkk = ASLine[kk].Split(',');
                if (tmpkk.Length != 7 && tmpkk.Length != 5)
                {
                    tmp[kk, 0] = "0";
                    tmp[kk, 1] = "0";
                    tmp[kk, 2] = "0";
                    tmp[kk, 3] = "0";
                    tmp[kk, 4] = "0";
                    continue;
                }

                tmp[kk, 0] = tmpkk[0];
                tmp[kk, 1] = tmpkk[1];
                tmp[kk, 2] = tmpkk[2];
                tmp[kk, 3] = tmpkk[3];
                tmp[kk, 4] = tmpkk[4];
            }

            for (var jj = 0; jj < ASOldLine.Length; jj++)
            {
                var tmpkk = ASOldLine[jj].Split(',');
                if (tmpkk.Length != 7 && tmpkk.Length != 5)
                {
                    tmpold[jj, 0] = "0";
                    tmpold[jj, 1] = "0";
                    tmpold[jj, 2] = "0";
                    tmpold[jj, 3] = "0";
                    tmpold[jj, 4] = "0";
                    continue;
                }

                tmpold[jj, 0] = tmpkk[0];
                tmpold[jj, 1] = tmpkk[1];
                tmpold[jj, 2] = tmpkk[2];
                tmpold[jj, 3] = tmpkk[3];
                tmpold[jj, 4] = tmpkk[4];
            }

            Parallel.For(0, tmp.GetLength(0), new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
            {
                if (tmp[i, 0] == "0")
                {
                    Dispatcher.Invoke(() => { Asprogressbar2.Value += p_val; });
                    return;
                }

                var countno = 0;
                var FindStr = tmp[i, 4];
                var ShaName = tmp[i, 0];
                Parallel.For(0, tmpold.GetLength(0), j =>
                {
                    if (tmpold[j, 0] == "0" || tmpold[j, 4] != FindStr)
                    {
                        lock (LockedList)
                        {
                            countno++;
                        }

                        return;
                    }

                    if (ShaName == tmpold[j, 0]) return;
                    Dispatcher.Invoke(() => { AsDownloadStatus.Items.Insert(0, "差异: " + FindStr.Replace(@"/", "@")); });
                    DownloadSelectFile(tmp, i, dest);
                });
                Dispatcher.Invoke(() => { Asprogressbar2.Value += p_val; });
                if (countno != tmpold.GetLength(0)) return;
                Dispatcher.Invoke(() => { AsDownloadStatus.Items.Insert(0, "新增: " + FindStr.Replace(@"/", "@")); });
                DownloadSelectFile(tmp, i, dest);
                Dispatcher.Invoke(() => { Asprogressbar2.Value += p_val; });
            });
        }

        private void DownloadSelectFile(string[,] ASFileLine, int index, string dest)
        {
            if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
            var downloadSHAName = ASFileLine[index, 0] + ".bin";
            var FileTrueName = ASFileLine[index, 4].Replace(@"/", "@");
            var downloadSHANameshort = downloadSHAName.Substring(0, 2);
            var url = request.cdnAddr + $"/NewResources/Android/{downloadSHANameshort}/{downloadSHAName}";
            byte[] Data;
            try
            {
                Data = HttpRequest.Get(url).ToBinary();
            }
            catch (Exception)
            {
                Dispatcher.Invoke(() => { AsDownloadStatus.Items.Insert(0, $"重试: {FileTrueName}"); });
                Thread.Sleep(5000);
                try
                {
                    Data = HttpRequest.Get(url).ToBinary();
                }
                catch (Exception e)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AsDownloadStatus.Items.Insert(0, $"{e}");
                        AsDownloadStatus.Items.Insert(0, $"失败: {FileTrueName}");
                    });
                    return;
                }
            }

            if (FileTrueName.Contains("Audio") || FileTrueName.Contains("Movie"))
                File.WriteAllBytes(dest + $"{FileTrueName}", Data);
            else
                File.WriteAllBytes(dest + $"{FileTrueName}.unity3d", CatAndMouseGame.MouseGame4(Data));
            GC.Collect();
        }

        private void ResetAS(object sender, RoutedEventArgs e)
        {
            Asprogressbar2.Value = 0.0;
            AsDownloadStatus.Items.Clear();
            ResetASBtn.IsEnabled = false;
            StartChayiDownload.IsEnabled = true;
            GC.Collect();
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

        private void LoadServerListTemp(object sender, RoutedEventArgs e)
        {
            request.cdnAddr = "http://line3-patch-fate.bilibiligame.net/2450";
            TryAsWithDate.IsEnabled = true;
            StartChayiDownload.IsEnabled = true;
        }
    }
}