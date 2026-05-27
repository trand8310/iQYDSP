
using Google.Protobuf;
using Iqiyi.Ssp.V51;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;


namespace MainClient.Common
{
    public class AdxHelper
    {

        private readonly ILogger _logger;
        private readonly IWritableOptions<AppSettings> _appSettings;
        private readonly IHttpClientFactory _httpClientFactory;

        public AdxHelper(IWritableOptions<AppSettings> appSettings, IHttpClientFactory httpClientFactory, ILogger<AdxHelper> logger)
        {
            _appSettings = appSettings;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }


        /// <summary>
        /// 获取任务
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public async Task<string> GetTaskAsync(string address)
        {
            var client = _httpClientFactory.CreateClient();
            try
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = await client.GetAsync(address);
                response.EnsureSuccessStatusCode();
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);

            }
            return null;
        }


        private ConcurrentDictionary<int, TaskStatItem> task_stat_dict = new ConcurrentDictionary<int, TaskStatItem>();

        public TaskStatItem GetOrAddTaskStatus(int taskid)
        {
            return task_stat_dict.GetOrAdd(taskid, new TaskStatItem());
        }
        public TaskStatItem UpdateTaskAll(int taskid, int all = 1)
        {
            return task_stat_dict.AddOrUpdate(taskid, new TaskStatItem(), (k, v) => v.AddAllCount(all));
        }
        public TaskStatItem UpdateTaskDsp(int taskid, int dsp = 1)
        {
            return task_stat_dict.AddOrUpdate(taskid, new TaskStatItem(), (k, v) => v.AddDspCount(dsp));
        }
        public TaskStatItem UpdateTaskDspClick(int taskid, int click = 1)
        {
            return task_stat_dict.AddOrUpdate(taskid, new TaskStatItem(0), (k, v) => v.AddDspClick(click));
        }



        #region 任务更新
        /// <summary>
        /// 更新总执行数量
        /// </summary>
        /// <param name="taskid"></param>
        /// <returns></returns>
        public async Task UpdateTaskStat()
        {
            //List<object> list = new List<object>();
            //Dictionary<int, TaskStatItem> tmp = new Dictionary<int, TaskStatItem>();
            //var client = _httpClientFactory.CreateClient();
            //foreach (var taskid in task_stat_dict.Keys)
            //{
            //    if (task_stat_dict.TryGetValue(taskid, out var stat))
            //    {
            //        var ack = stat.ack;
            //        var dspCount = stat.dspCount;
            //        var dspClick = stat.dspClick;
            //        list.Add(new { taskid = taskid, ack = ack, dsp_count = dspCount, dsp_click_count = dspClick });
            //        tmp.Add(taskid, new TaskStatItem(ack, dspCount, dspClick));
            //    }
            //}
            //var postData = JsonConvert.SerializeObject(list);
            //HttpContent content = new StringContent(postData);
            //content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            //var response = await client.PostAsync($"{_appSettings.Value.TaskApiUrl}?action=update-task-stat", content);
            //response.EnsureSuccessStatusCode();
            //await response.Content.ReadAsStringAsync();

            //foreach (var taskid in tmp.Keys)
            //{
            //    task_stat_dict.AddOrUpdate(taskid, new TaskStatItem(0), (k, v) => v.UpdateAll(tmp[taskid].ack, tmp[taskid].dspCount, tmp[taskid].dspClick));
            //}
            await Task.CompletedTask;
        }



        /// <summary>
        /// 更新总的点击数量
        /// </summary>
        /// <param name="taskid"></param>
        /// <returns></returns>
        public async Task<string> UpdateTaskClickNum(string taskid)
        {
            var client = _httpClientFactory.CreateClient();
            HttpResponseMessage response = await client.GetAsync($"{_appSettings.Value.TaskApiUrl}?action=update-task-click-num&taskid={taskid}&_t={System.DateTime.Now.Ticks}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// 更新字段及获取值
        /// </summary>
        /// <param name="taskid"></param>
        /// <returns></returns>
        public async Task<string> UpdateTaskStateNum(string taskid, string keys, string value = "1", string fields = "*")
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                HttpResponseMessage response = await client.GetAsync($"{_appSettings.Value.TaskApiUrl}?action=updateTaskstate&taskid={taskid}&keys={keys}&value={value}&fields={fields}&_t={System.DateTime.Now.Ticks}");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception)
            {
                return null;
            }

        }

        #endregion





        /// <summary>
        ///短信通知
        /// </summary>
        /// <param name="name"></param>
        /// <param name="phone"></param>
        /// <returns></returns>
        public async Task<string> SendSMS(string name, string phone)
        {
            var client = _httpClientFactory.CreateClient();
            try
            {
                var formData = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("name",name),
                    new KeyValuePair<string, string>("phone", phone)
                });
                var response = await client.PostAsync("http://111.73.45.147/sendsms.php", formData);
                response.EnsureSuccessStatusCode();
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();

                    return result;
                }
            }
            catch (WebException ex)
            {
                Debug.WriteLine(ex.Message);

            }
            return null;
        }

        static double CalculatePPI(double widthPixels, double heightPixels, double diagonalInches)
        {
            if (diagonalInches <= 0)
            {
                throw new ArgumentException("对角线尺寸必须大于 0");
            }
            // 使用勾股定理计算像素对角线长度
            double diagonalPixels = Math.Sqrt(Math.Pow(widthPixels, 2) + Math.Pow(heightPixels, 2));

            // 计算 PPI
            return diagonalPixels / diagonalInches;
        }


        //public static SemaphoreSlim _mutex = new SemaphoreSlim(System.Environment.ProcessorCount);


        private static HttpClient CreateHttpClient(string proxy, bool isProxyMode)
        {
            if (!isProxyMode)
            {
                return new HttpClient();
            }

            var webProxy = new WebProxy(proxy, BypassOnLocal: false);

            var handler = new HttpClientHandler
            {
                Proxy = webProxy,
                UseProxy = true
            };

            return new HttpClient(handler);
        }

        private static int GetRandomCarrier(out string carrierName)
        {
            var carrier = new int[]
            {
                1, 1, 1,
                2, 2,
                3,
                1, 1, 1,
                2, 2,
                3
            }[Random.Shared.Next(0, 12)];

            carrierName = carrier switch
            {
                1 => "中国移动",
                2 => "中国联通",
                3 => "中国电信",
                _ => "unknown"
            };

            return carrier;
        }

        private static BidRequest.Types.Device.Types.ConnectionType GetRandomConnectionType()
        {
            var value = new int[]
            {
                2, 2, 2,
                6, 6,
                7, 7,
                2, 2, 2,
                6, 6,
                7, 7
            }[Random.Shared.Next(0, 14)];

            return (BidRequest.Types.Device.Types.ConnectionType)value;
        }

        private static bool ToBool(JToken? token)
        {
            if (token == null)
                return false;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            if (token.Type == JTokenType.Integer)
                return token.Value<int>() != 0;

            var str = token.ToString();

            if (string.Equals(str, "true", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(str, "1", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }



        static string GetBidResponseStatusMessage(int status)
        {
            return status switch
            {
                1 => "required必填信息缺失",
                2 => "贴片缺失video",
                3 => "信息流缺失native",
                4 => "native中video或者img至少填一项",
                5 => "用户ip异常",
                6 => "devicetype不合法",
                _ => $"未知错误，Status={status}"
            };
        }


        public async Task<JObject?> GetAdRequest(
           JObject task,
           JObject adParam,
           JObject dev,
           OSType os,
           string realip,
           string proxy,
           JObject? ipLocation,
           bool isProxyMode)
        {
            try
            {
                var ua = dev["ua"]?.ToString() ?? "";

                var url = task["url"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(url))
                    throw new Exception("task.url 不能为空");

                if (adParam.ContainsKey("account_id"))
                {
                    url += $"a={adParam["account_id"]}";
                }
                if (adParam.ContainsKey("access_token"))
                {
                    url += $"&token={adParam["access_token"]}";
                }

                //adParam["adzone_id"] = "1641566616277124";
                //url = "http://api-test-ssp.iqiyi.com/bid?a=1623148501483523&adtype=WebView";



                //adParam["adzone_id"] = "1641566616277124";// Android 开屏：1641566616277124
                //adParam["adzone_id"] = "1623148675781639";//Android 信息流图片：1623148675781639
                //adParam["adzone_id"] = "1623148712439937";//Android 信息流视频：1623148712439937

                //adParam["adzone_id"] = "1641566679670913";// iOS 开屏：1641566679670913
                //adParam["adzone_id"] = "1623148829370370";//iOS 信息流图片：1623148829370370
                //adParam["adzone_id"] = "1623148840543367";//iOS 信息流视频：1623148840543367

                var hash_code = Math.Abs(($"{dev}_{realip}").GetHashCode());



                var age = hash_code % 7;
                var gender = new string[] { "M", "F", "" }[hash_code % 3];

                var bidRequest = new BidRequest
                {
                    Id = Guid.NewGuid().ToString("N"),

                    // 爱奇艺 proto 注释要求 millisecond
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                    // 0 = site, 1 = app
                    Resourcetype = adParam["resourcetype"]?.Value<int>() ?? 1,

                    Site = new BidRequest.Types.Site(),

                    App = new BidRequest.Types.App
                    {
                        Name = adParam["app_name"]?.ToString() ?? "",
                        Bundle = adParam["app_bundle"]?.ToString() ?? "",
                        Ver = adParam["app_ver"]?.ToString() ?? ""
                    },

                    User = ((hash_code % 2) == 0) ? new BidRequest.Types.User()
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Age = age,
                        Gender = gender,
                    } : new BidRequest.Types.User(),
                };

                //var user = new BidRequest.Types.User();
                //user.Id = Guid.NewGuid().ToString("N");
                //bidRequest.User = user;


                var imp = new BidRequest.Types.Imp
                {
                    AdzoneId = adParam["adzone_id"]?.ToString() ?? "",

                };
                if (adParam["ad_type"] != null)
                {
                    var ad_type = (BidRequest.Types.Imp.Types.AdType)adParam["ad_type"]!.Value<int>();
                    imp.AdType = ad_type;
                    if (ad_type == BidRequest.Types.Imp.Types.AdType.Opening)
                    {
                        imp.MediaAdzoneId = adParam["media_adzone_id"]?.ToString() ?? "";
                        imp.Bidfloor = adParam["bidfloor"]?.Value<int>() ?? 0;
                        imp.MaterialType = adParam["material_type"]?.Value<int>() ?? 0;

                    }
                    else if (ad_type == BidRequest.Types.Imp.Types.AdType.Feeds)
                    {
                        //int sw = dev.SelectToken("sw")?.Value<int>();
                        //int sh = dev.SelectToken("sh")?.Value<int>();

                        var native = new BidRequest.Types.Imp.Types.Native
                        {
                            TitleLen = adParam["title_len"]?.Value<int>() ?? 20,
                            Maxadscount = adParam["maxadscount"]?.Value<int>() ?? 1
                        };
                        // 图片信息流
                        var imgs = adParam["imgs"] as JArray;
                        if (imgs != null && imgs.Count > 0)
                        {
                            foreach (var imgToken in imgs)
                            {
                                if (imgToken is not JObject imgParam)
                                    continue;

                                var img = new BidRequest.Types.Imp.Types.Native.Types.Image
                                {
                                    Type = (BidRequest.Types.Imp.Types.Native.Types.Image.Types.ImageAssetType)
                                        (imgParam["type"]?.Value<int>() ?? 3), // 默认 MAIN

                                    W = imgParam["w"]?.Value<int>() ?? 0,
                                    H = imgParam["h"]?.Value<int>() ?? 0,
                                    Wmin = imgParam["wmin"]?.Value<int>() ?? 0,
                                    Hmin = imgParam["hmin"]?.Value<int>() ?? 0
                                };
                                native.Imgs.Add(img);
                            }
                        }

                        // 视频信息流
                        var videoParam = adParam["video"] as JObject;
                        if (videoParam != null)
                        {
                            native.Video = new BidRequest.Types.Imp.Types.Native.Types.Video
                            {
                                W = videoParam["w"]?.Value<int>() ?? 0,
                                H = videoParam["h"]?.Value<int>() ?? 0,
                                Minduration = videoParam["minduration"]?.Value<int>() ?? 5,
                                Maxduration = videoParam["maxduration"]?.Value<int>() ?? 60,
                                Format = (BidRequest.Types.Imp.Types.VideoFormat)
                                    (videoParam["format"]?.Value<int>() ?? 2) // 默认 VIDEO_MP4
                            };
                        }
                        // Feeds 至少要有图片或视频
                        if (native.Imgs.Count == 0 && native.Video == null)
                        {
                            // 给一个默认主图，避免 REQUEST_FEEDS_LACK_VIDEO_OR_IMG
                            native.Imgs.Add(new BidRequest.Types.Imp.Types.Native.Types.Image
                            {
                                Type = BidRequest.Types.Imp.Types.Native.Types.Image.Types.ImageAssetType.Main,
                                W = adParam["w"]?.Value<int>() ?? 640,
                                H = adParam["h"]?.Value<int>() ?? 360,
                                Wmin = adParam["wmin"]?.Value<int>() ?? 320,
                                Hmin = adParam["hmin"]?.Value<int>() ?? 180
                            });
                        }
                        imp.Native = native;

                    }
                    else if (ad_type == BidRequest.Types.Imp.Types.AdType.Roll)
                    {
                        imp.MediaAdzoneId = adParam["media_adzone_id"]?.ToString() ?? "";
                        imp.Bidfloor = adParam["bidfloor"]?.Value<int>() ?? 0;
                        imp.MaterialType = adParam["material_type"]?.Value<int>() ?? 0;

                        var videoParam = adParam["video"] as JObject;
                        imp.Video = new BidRequest.Types.Imp.Types.Video
                        {
                            W = videoParam?["w"]?.Value<int>()
                                ?? adParam["w"]?.Value<int>()
                                ?? 640,

                            H = videoParam?["h"]?.Value<int>()
                                ?? adParam["h"]?.Value<int>()
                                ?? 360,

                            Minduration = videoParam?["minduration"]?.Value<int>()
                                ?? adParam["minduration"]?.Value<int>()
                                ?? 5,

                            Maxduration = videoParam?["maxduration"]?.Value<int>()
                                ?? adParam["maxduration"]?.Value<int>()
                                ?? 30,

                            Format = (BidRequest.Types.Imp.Types.VideoFormat)
                                (videoParam?["format"]?.Value<int>()
                                 ?? adParam["format"]?.Value<int>()
                                 ?? 2)
                        };
                    }
                }



                if (adParam["allowed_action_type"] is JArray allowedArray)
                {
                    foreach (var item in allowedArray)
                    {
                        imp.AllowedActionType.Add(item.Value<int>());
                    }
                }
                else if (adParam["allowed_action_type"] != null)
                {
                    imp.AllowedActionType.Add(adParam["allowed_action_type"]!.Value<int>());
                }

                // 如果你的广告位是视频广告，需要按文档补 video
                //if (adParam["video"] is JObject videoObj)
                //{
                //    imp.Video = new BidRequest.Types.Imp.Types.Video
                //    {
                //        W = videoObj["w"]?.Value<int>() ?? adParam["video_w"]?.Value<int>() ?? 0,
                //        H = videoObj["h"]?.Value<int>() ?? adParam["video_h"]?.Value<int>() ?? 0,
                //        Minduration = videoObj["minduration"]?.Value<int>() ?? 5,
                //        Maxduration = videoObj["maxduration"]?.Value<int>() ?? 60,
                //        Startdelay = videoObj["startdelay"]?.Value<int>() ?? 0,
                //        Linearity = (BidRequest.Types.Imp.Types.Video.Types.VideoLinearity)
                //            (videoObj["linearity"]?.Value<int>() ?? 1),
                //        AcceptedCreativeTypes = videoObj["accepted_creative_types"]?.Value<int>() ?? 3,
                //        Maxadscount = videoObj["maxadscount"]?.Value<int>() ?? 1,
                //        Format = (BidRequest.Types.Imp.Types.VideoFormat)
                //            (videoObj["format"]?.Value<int>() ?? 2)
                //    };
                //}

                // 如果你的广告位是信息流/native，需要按文档补 native
                //if (adParam["native"] is JObject nativeObj)
                //{
                //    var native = new BidRequest.Types.Imp.Types.Native
                //    {
                //        TitleLen = nativeObj["title_len"]?.Value<int>() ?? 30,
                //        Maxadscount = nativeObj["maxadscount"]?.Value<int>() ?? 1
                //    };

                //    if (nativeObj["imgs"] is JArray imgs)
                //    {
                //        foreach (var imgToken in imgs)
                //        {
                //            if (imgToken is not JObject imgObj)
                //                continue;

                //            native.Imgs.Add(new BidRequest.Types.Imp.Types.Native.Types.Image
                //            {
                //                Type = (BidRequest.Types.Imp.Types.Native.Types.Image.Types.ImageAssetType)
                //                    (imgObj["type"]?.Value<int>() ?? 3),
                //                W = imgObj["w"]?.Value<int>() ?? 0,
                //                H = imgObj["h"]?.Value<int>() ?? 0,
                //                Wmin = imgObj["wmin"]?.Value<int>() ?? 0,
                //                Hmin = imgObj["hmin"]?.Value<int>() ?? 0
                //            });
                //        }
                //    }

                //    imp.Native = native;
                //}

                bidRequest.Imp.Add(imp);

                var geo = new BidRequest.Types.Geo
                {
                    Type = BidRequest.Types.Geo.Types.LocationType.Ip
                };

                if (ipLocation != null)
                {
                    geo.Lat = ipLocation["lat"]?.Value<double>() ?? 0;
                    geo.Lon = ipLocation["lon"]?.Value<double>() ?? 0;

                    if (ipLocation["country"] != null)
                        geo.Country = ipLocation["country"]!.ToString();

                    if (ipLocation["prov"] != null)
                        geo.Prov = ipLocation["prov"]!.ToString();

                    if (ipLocation["city"] != null)
                        geo.City = ipLocation["city"]!.ToString();

                    if (ipLocation["district"] != null)
                        geo.District = ipLocation["district"]!.ToString();
                }

                var device = new BidRequest.Types.Device
                {
                    Ua = ua,
                    Ip = realip,
                    Geo = geo,

                    Make = dev["make"]?.Value<string>() ?? "",
                    Model = dev["model"]?.Value<string>() ?? "",
                    Osv = dev["osv"]?.Value<string>() ?? "",

                    W = dev["sw"]?.Value<int>() ?? 0,
                    H = dev["sh"]?.Value<int>() ?? 0,

                    Carrier = GetRandomCarrier(out var carrierName),
                    CarrierName = carrierName,

                    Connectiontype = GetRandomConnectionType(),

                    CountryCode = dev["country_code"]?.Value<string>() ?? "GB",
                    TimeZoneSec = dev["time_zone_sec"]?.Value<string>() ?? "28800",
                    DeviceLanguage = dev["device_language"]?.Value<string>() ?? "zh-Hans-CN",
                    MachineOfDevice = dev["machine_of_device"]?.Value<string>() ?? dev["model"]?.ToString() ?? "",

                    Devicetype = BidRequest.Types.Device.Types.DeviceType.Phone
                };

                if (os == OSType.ANDROID)
                {
                    device.Os = "Android";

                    var oaid = dev["oaid"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(oaid))
                    {
                        oaid = oaid.Trim().ToLowerInvariant();
                        device.Oaid = oaid;
                        device.OaidMd5 = CommonHelper.MD5Hash(oaid).ToLowerInvariant();
                    }

                    var androidid = dev["androidid"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(androidid))
                    {
                        androidid = androidid.Trim().ToLowerInvariant();

                        // proto 里字段名是 androidid，不是 android_id
                        device.Androidid = androidid;
                        device.AndroididMd5 = CommonHelper.MD5Hash(androidid).ToLowerInvariant();
                    }

                    var mac = dev["mac"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(mac))
                    {
                        var macUpper = mac.Trim().ToUpperInvariant();
                        device.Mac = macUpper;

                        var processedMac = macUpper
                            .Replace(":", "")
                            .Replace("-", "");

                        device.ProcessedMacMd5 = CommonHelper.MD5Hash(processedMac).ToLowerInvariant();
                    }
                }
                else if (os == OSType.IOS)
                {
                    device.Os = "iOS";

                    var idfa = dev["idfa"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(idfa))
                    {
                        idfa = idfa.Trim().ToUpperInvariant();
                        device.Idfa = idfa;
                        device.IdfaMd5 = CommonHelper.MD5Hash(idfa).ToLowerInvariant();
                    }

                    if (dev["boot_mark"] != null)
                        device.BootMark = dev["boot_mark"]!.ToString();

                    if (dev["update_mark"] != null)
                        device.UpdateMark = dev["update_mark"]!.ToString();

                    if (dev["mnt_id"] != null)
                        device.MntId = dev["mnt_id"]!.ToString();

                    if (dev["file_init_time"] != null)
                        device.FileInitTime = dev["file_init_time"]!.ToString();

                    if (dev["device_name_md5"] != null)
                        device.DeviceNameMd5 = dev["device_name_md5"]!.ToString();
                }

                if (dev["disk_total"] != null)
                    device.DiskTotal = dev["disk_total"]!.Value<long>();
                else
                {
                    var storage = dev["storage"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(storage))
                    {
                        storage = "128,256,512";
                    }
                    var storage_values = storage.Split(',');
                    var storage_value = storage_values[CommonHelper.RandomRange(0, storage_values.Length)];
                    if (storage_value.Contains("GB"))
                        storage_value = storage_value.Replace("GB", "");
                    var disk_total = 1073741824 * long.Parse(storage_value);
                    device.DiskTotal = disk_total;
                }
                if (dev["mem_total"] != null)
                {
                    device.MemTotal = dev["mem_total"]!.Value<long>();
                }
                else
                {
                    var ram = dev["ram"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(ram))
                    {
                        ram = "4,6,8,8,8,8,8,12";
                    }
                    var ram_values = ram.Split(',');
                    var ram_value = ram_values[CommonHelper.RandomRange(0, ram_values.Length)];
                    if (ram_value.Contains("GB"))
                        ram_value = ram_value.Replace("GB", "");
                    var mem_total = 1073741824 * long.Parse(ram_value);
                    device.MemTotal = mem_total;
                }


                if (dev["ipv6"] != null)
                    device.Ipv6 = dev["ipv6"]!.ToString();

                bidRequest.Device = device;

                if (adParam.ContainsKey("test"))
                {
                    bidRequest.Test = ToBool(adParam["test"]);
                }

                // deduplicated_ids
                if (adParam["deduplicated_ids"] is JArray deduplicatedIds)
                {
                    foreach (var item in deduplicatedIds)
                    {
                        if (item is not JObject obj)
                            continue;

                        bidRequest.DeduplicatedIds.Add(new BidRequest.Types.DeduplicatedId
                        {
                            Type = obj["type"]?.Value<int>() ?? 0,
                            Id = obj["id"]?.ToString() ?? ""
                        });
                    }
                }

                // extended_entries
                if (adParam["extended_entries"] is JObject extendedObj)
                {
                    foreach (var prop in extendedObj.Properties())
                    {
                        bidRequest.ExtendedEntries.Add(new Entry
                        {
                            Key = prop.Name,
                            Value = prop.Value?.ToString() ?? ""
                        });
                    }
                }
                else if (adParam["extended_entries"] is JArray extendedArray)
                {
                    foreach (var item in extendedArray)
                    {
                        if (item is not JObject obj)
                            continue;

                        bidRequest.ExtendedEntries.Add(new Entry
                        {
                            Key = obj["key"]?.ToString() ?? "",
                            Value = obj["value"]?.ToString() ?? ""
                        });
                    }
                }



                //a=1819483575856516&token=bdc0f9bee8514f36bfa61c0c3a7b2b18


                //url = $"http://api-test-ssp.iqiyi.com/bid?a=1819483575856516&token=bdc0f9bee8514f36bfa61c0c3a7b2b18";
                //url = $"http://api-test-ssp.iqiyi.com/bid?a=1819483575856516&adtype=WebView";
                //url = "http://api-test-ssp.iqiyi.com/bid?a=1819483575856516&adtype=WebView";
                //url = "http://api-test-ssp.iqiyi.com/bid?a=1623148501483523&adtype=WebView";
                using var client = CreateHttpClient(proxy, isProxyMode);
                client.Timeout = TimeSpan.FromSeconds(15);

                byte[] postBytes = bidRequest.ToByteArray();

                using var content = new ByteArrayContent(postBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd(ua);
                client.DefaultRequestHeaders.ConnectionClose = false;

                using var response = await client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                if (response.StatusCode == HttpStatusCode.NoContent)
                    return null;

                byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();

                if (responseBytes.Length == 0)
                    return null;

                // 返回也是 protobuf，所以解析 BidResponse
                var bidResponse = BidResponse.Parser.ParseFrom(responseBytes);
                if (bidResponse.Status != 0)
                {
                    throw new InvalidOperationException(GetBidResponseStatusMessage(bidResponse.Status));
                }

                // 为了保持你原来的方法返回 JObject，这里转成 JSON 再 JObject.Parse
                var jsonText = JsonFormatter.Default.Format(bidResponse);
                return JObject.Parse(jsonText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                throw;
            }
        }

    }

    public class TaskStatItem
    {
        public TaskStatItem(int all = 0, int dsp = 0, int click = 0, int pending = 0)
        {
            allCount = all;
            dspCount = dsp;
            dspClick = click;
            pendingClick = pending;
            adxCount = 0;

        }
        public int allCount;
        public int adxCount;
        public int dspCount;
        public int dspClick;
        public int pendingClick;

        public TaskStatItem AddAllCount(int value)
        {
            Interlocked.Add(ref allCount, value);
            return this;
        }
        public TaskStatItem AddAdxCount(int value)
        {
            Interlocked.Add(ref adxCount, value);
            return this;
        }
        public TaskStatItem AddDspCount(int value)
        {
            Interlocked.Add(ref dspCount, value);
            return this;
        }
        public TaskStatItem AddDspClick(int value)
        {
            Interlocked.Add(ref dspClick, value);
            return this;
        }
        public TaskStatItem AddPendingClick(int value)
        {
            Interlocked.Add(ref pendingClick, value);
            return this;
        }
    }
}

