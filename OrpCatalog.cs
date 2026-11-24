using System;
using System.Collections.Generic;
using System.Drawing;

namespace MeteoSrazky
{
    public class OrpAreaInfo
    {
        public string Name { get; set; }
        public string RegionCode { get; set; }
        public double CenterLat { get; set; }
        public double CenterLon { get; set; }
        public List<PointF> SamplePointsLatLon { get; set; }

        public OrpAreaInfo()
        {
            SamplePointsLatLon = new List<PointF>();
        }
    }

    public static class OrpCatalog
    {
        private struct OrpSeed
        {
            public string Name;
            public double Lat;
            public double Lon;
            public OrpSeed(string name, double lat, double lon)
            {
                Name = name;
                Lat = lat;
                Lon = lon;
            }
        }

        // Complete list of all 206 official Czech ORP administrative municipalities
        private static readonly OrpSeed[] OfficialOrpSeeds = new OrpSeed[]
        {
            new OrpSeed("Praha", 50.0755, 14.4378),
            new OrpSeed("Aš", 50.2239, 12.1950),
            new OrpSeed("Benešov", 49.7816, 14.6870),
            new OrpSeed("Beroun", 49.9638, 14.0720),
            new OrpSeed("Bílina", 50.5485, 13.7754),
            new OrpSeed("Bílovec", 49.7570, 18.0158),
            new OrpSeed("Blansko", 49.3631, 16.6445),
            new OrpSeed("Blatná", 49.4249, 13.8817),
            new OrpSeed("Blovice", 49.5823, 13.5403),
            new OrpSeed("Bohumín", 49.9043, 18.3575),
            new OrpSeed("Bojkovice", 49.0389, 17.8149),
            new OrpSeed("Boskovice", 49.4875, 16.6600),
            new OrpSeed("Brandýs n. L.-St. Boleslav", 50.1872, 14.6633),
            new OrpSeed("Brno", 49.1951, 16.6068),
            new OrpSeed("Broumov", 50.5857, 16.3318),
            new OrpSeed("Bruntál", 49.9884, 17.4648),
            new OrpSeed("Břeclav", 48.7590, 16.8820),
            new OrpSeed("Bučovice", 49.1490, 17.0017),
            new OrpSeed("Bystřice n. Pernštejnem", 49.5229, 16.2615),
            new OrpSeed("Bystřice p. Hostýnem", 49.3999, 17.6743),
            new OrpSeed("Čáslav", 49.9110, 15.3897),
            new OrpSeed("Černošice", 49.9606, 14.3193),
            new OrpSeed("Česká Lípa", 50.6855, 14.5376),
            new OrpSeed("Česká Třebová", 49.9044, 16.4441),
            new OrpSeed("České Budějovice", 48.9745, 14.4743),
            new OrpSeed("Český Brod", 50.0742, 14.8608),
            new OrpSeed("Český Krumlov", 48.8127, 14.3175),
            new OrpSeed("Český Těšín", 49.7461, 18.6261),
            new OrpSeed("Dačice", 49.0811, 15.4372),
            new OrpSeed("Děčín", 50.7821, 14.2148),
            new OrpSeed("Dobruška", 50.2917, 16.1597),
            new OrpSeed("Dobříš", 49.7812, 14.1675),
            new OrpSeed("Domažlice", 49.4406, 12.9298),
            new OrpSeed("Dvůr Králové n. Labem", 50.4317, 15.8140),
            new OrpSeed("Frenštát p. Radhoštěm", 49.5484, 18.2108),
            new OrpSeed("Frýdek-Místek", 49.6853, 18.3504),
            new OrpSeed("Frýdlant", 50.9214, 15.0799),
            new OrpSeed("Frýdlant n. Ostravicí", 49.5928, 18.3597),
            new OrpSeed("Havířov", 49.7798, 18.4369),
            new OrpSeed("Havlíčkův Brod", 49.6078, 15.5807),
            new OrpSeed("Hlinsko", 49.7623, 15.9076),
            new OrpSeed("Hlučín", 49.8980, 18.1920),
            new OrpSeed("Hodonín", 48.8489, 17.1324),
            new OrpSeed("Holešov", 49.3333, 17.5783),
            new OrpSeed("Holice", 50.0666, 15.9860),
            new OrpSeed("Horažďovice", 49.3208, 13.7008),
            new OrpSeed("Hořice", 50.3661, 15.6319),
            new OrpSeed("Hořovice", 49.8364, 13.9028),
            new OrpSeed("Horšovský Týn", 49.5297, 12.9442),
            new OrpSeed("Hradec Králové", 50.2104, 15.8252),
            new OrpSeed("Hranice", 49.5480, 17.7347),
            new OrpSeed("Humpolec", 49.5416, 15.3593),
            new OrpSeed("Hustopeče", 48.9408, 16.7376),
            new OrpSeed("Cheb", 50.0796, 12.3739),
            new OrpSeed("Chomutov", 50.4605, 13.4178),
            new OrpSeed("Chotěboř", 49.7208, 15.6702),
            new OrpSeed("Chrudim", 49.9511, 15.7956),
            new OrpSeed("Ivančice", 49.1015, 16.3776),
            new OrpSeed("Jablonec n. Nisou", 50.7243, 15.1711),
            new OrpSeed("Jablunkov", 49.5767, 18.7646),
            new OrpSeed("Jaroměř", 50.3551, 15.9209),
            new OrpSeed("Jeseník", 50.2294, 17.2046),
            new OrpSeed("Jičín", 50.4370, 15.3516),
            new OrpSeed("Jihlava", 49.3961, 15.5912),
            new OrpSeed("Jilemnice", 50.6089, 15.5065),
            new OrpSeed("Jindřichův Hradec", 49.1440, 15.0030),
            new OrpSeed("Kadaň", 50.3833, 13.2713),
            new OrpSeed("Kaplice", 48.7378, 14.4946),
            new OrpSeed("Karlovy Vary", 50.2327, 12.8712),
            new OrpSeed("Karviná", 49.8540, 18.5416),
            new OrpSeed("Kladno", 50.1473, 14.1028),
            new OrpSeed("Klatovy", 49.3955, 13.2951),
            new OrpSeed("Kolín", 50.0281, 15.2006),
            new OrpSeed("Konice", 49.5892, 16.8878),
            new OrpSeed("Kopřivnice", 49.5995, 18.1448),
            new OrpSeed("Kostelec n. Orlicí", 50.1228, 16.2121),
            new OrpSeed("Králíky", 50.0837, 16.7606),
            new OrpSeed("Kralovice", 49.9813, 13.4875),
            new OrpSeed("Kralupy n. Vltavou", 50.2411, 14.3114),
            new OrpSeed("Kraslice", 50.3237, 12.5180),
            new OrpSeed("Kravaře", 49.9321, 18.0047),
            new OrpSeed("Krnov", 50.0897, 17.7039),
            new OrpSeed("Kroměříž", 49.2979, 17.3931),
            new OrpSeed("Kuřim", 49.2989, 16.5314),
            new OrpSeed("Kutná Hora", 49.9484, 15.2682),
            new OrpSeed("Kyjov", 49.0102, 17.1225),
            new OrpSeed("Lanškroun", 49.9122, 16.6119),
            new OrpSeed("Liberec", 50.7663, 15.0543),
            new OrpSeed("Lipník n. Bečvou", 49.5272, 17.5859),
            new OrpSeed("Litoměřice", 50.5335, 14.1318),
            new OrpSeed("Litomyšl", 49.8687, 16.3129),
            new OrpSeed("Litovel", 49.7012, 17.0762),
            new OrpSeed("Litvínov", 50.6008, 13.6112),
            new OrpSeed("Louny", 50.3570, 13.7967),
            new OrpSeed("Lovosice", 50.5150, 14.0510),
            new OrpSeed("Luhačovice", 49.0998, 17.7575),
            new OrpSeed("Lysá n. Labem", 50.2015, 14.8329),
            new OrpSeed("Mariánské Lázně", 49.9646, 12.7012),
            new OrpSeed("Mělník", 50.3505, 14.4741),
            new OrpSeed("Mikulov", 48.8056, 16.6378),
            new OrpSeed("Milevsko", 49.4509, 14.3601),
            new OrpSeed("Mladá Boleslav", 50.4113, 14.9032),
            new OrpSeed("Mnichovo Hradiště", 50.5273, 14.9714),
            new OrpSeed("Mohelnice", 49.7769, 16.9195),
            new OrpSeed("Moravská Třebová", 49.7579, 16.6642),
            new OrpSeed("Moravské Budějovice", 49.0519, 15.8082),
            new OrpSeed("Moravský Krumlov", 49.0483, 16.3119),
            new OrpSeed("Most", 50.5030, 13.6362),
            new OrpSeed("Náchod", 50.4167, 16.1629),
            new OrpSeed("Nepomuk", 49.4864, 13.5821),
            new OrpSeed("Neratovice", 50.2593, 14.5176),
            new OrpSeed("Nová Paka", 50.4959, 15.5146),
            new OrpSeed("Nové Město na Moravě", 49.5614, 16.0742),
            new OrpSeed("Nové Město n. Metují", 50.3446, 16.1516),
            new OrpSeed("Nový Bor", 50.7576, 14.5556),
            new OrpSeed("Nový Bydžov", 50.2413, 15.4908),
            new OrpSeed("Nový Jičín", 49.5944, 18.0103),
            new OrpSeed("Nymburk", 50.1860, 15.0417),
            new OrpSeed("Nýřany", 49.7131, 13.2089),
            new OrpSeed("Odry", 49.6626, 17.8308),
            new OrpSeed("Olomouc", 49.5938, 17.2509),
            new OrpSeed("Opava", 49.9387, 17.9026),
            new OrpSeed("Orlová", 49.8453, 18.4301),
            new OrpSeed("Ostrov", 50.3059, 12.9390),
            new OrpSeed("Ostrava", 49.8209, 18.2625),
            new OrpSeed("Otrokovice", 49.2097, 17.5308),
            new OrpSeed("Pacov", 49.4706, 15.0017),
            new OrpSeed("Pardubice", 50.0343, 15.7812),
            new OrpSeed("Pelhřimov", 49.4313, 15.2234),
            new OrpSeed("Písek", 49.3088, 14.1475),
            new OrpSeed("Plzeň", 49.7384, 13.3736),
            new OrpSeed("Podbořany", 50.2294, 13.4119),
            new OrpSeed("Poděbrady", 50.1424, 15.1188),
            new OrpSeed("Pohořelice", 48.9835, 16.5245),
            new OrpSeed("Polička", 49.7145, 16.2654),
            new OrpSeed("Prachatice", 49.0129, 13.9975),
            new OrpSeed("Prostějov", 49.4719, 17.1118),
            new OrpSeed("Přelouč", 50.0398, 15.5603),
            new OrpSeed("Přerov", 49.4551, 17.4509),
            new OrpSeed("Přeštice", 49.5729, 13.3338),
            new OrpSeed("Příbram", 49.6899, 14.0104),
            new OrpSeed("Rakovník", 50.1037, 13.7334),
            new OrpSeed("Říčany", 49.9917, 14.6543),
            new OrpSeed("Rokycany", 49.7427, 13.5946),
            new OrpSeed("Rosice", 49.1815, 16.3878),
            new OrpSeed("Roudnice n. Labem", 50.4253, 14.2618),
            new OrpSeed("Rožnov p. Radhoštěm", 49.4585, 18.1430),
            new OrpSeed("Rumburk", 50.9515, 14.5570),
            new OrpSeed("Rychnov n. Kněžnou", 50.1628, 16.2749),
            new OrpSeed("Rýmařov", 49.9318, 17.2718),
            new OrpSeed("Sedlčany", 49.6606, 14.4267),
            new OrpSeed("Semily", 50.6019, 15.3355),
            new OrpSeed("Slaný", 50.2305, 14.0870),
            new OrpSeed("Slavkov u Brna", 49.1533, 16.8765),
            new OrpSeed("Soběslav", 49.2599, 14.7186),
            new OrpSeed("Sokolov", 50.1813, 12.6401),
            new OrpSeed("Stod", 49.6392, 13.1646),
            new OrpSeed("Strakonice", 49.2614, 13.9024),
            new OrpSeed("Stříbro", 49.7530, 12.9999),
            new OrpSeed("Sušice", 49.2311, 13.5202),
            new OrpSeed("Světlá n. Sázavou", 49.6681, 15.4040),
            new OrpSeed("Svitavy", 49.7559, 16.4683),
            new OrpSeed("Šlapanice", 49.1686, 16.7271),
            new OrpSeed("Šternberk", 49.7305, 17.2989),
            new OrpSeed("Šumperk", 49.9653, 16.9706),
            new OrpSeed("Tábor", 49.4144, 14.6578),
            new OrpSeed("Tachov", 49.7953, 12.6337),
            new OrpSeed("Tanvald", 50.7374, 15.3057),
            new OrpSeed("Telč", 49.1842, 15.4528),
            new OrpSeed("Teplice", 50.6404, 13.8245),
            new OrpSeed("Tišnov", 49.3489, 16.4244),
            new OrpSeed("Trhové Sviny", 48.8424, 14.6392),
            new OrpSeed("Trutnov", 50.5610, 15.9127),
            new OrpSeed("Třebíč", 49.2149, 15.8817),
            new OrpSeed("Třeboň", 49.0036, 14.7706),
            new OrpSeed("Třinec", 49.6776, 18.6708),
            new OrpSeed("Turnov", 50.5873, 15.1569),
            new OrpSeed("Týn n. Vltavou", 49.2235, 14.4206),
            new OrpSeed("Uherské Hradiště", 49.0698, 17.4597),
            new OrpSeed("Uherský Brod", 49.0251, 17.6465),
            new OrpSeed("Uničov", 49.7709, 17.1214),
            new OrpSeed("Ústí n. Labem", 50.6607, 14.0323),
            new OrpSeed("Ústí n. Orlicí", 49.9739, 16.3936),
            new OrpSeed("Valašské Klobouky", 49.1404, 18.0080),
            new OrpSeed("Valašské Meziříčí", 49.4718, 17.9711),
            new OrpSeed("Varnsdorf", 50.9115, 14.6182),
            new OrpSeed("Velké Meziříčí", 49.3553, 16.0123),
            new OrpSeed("Veselí n. Moravou", 48.9536, 17.3765),
            new OrpSeed("Vimperk", 49.0585, 13.7829),
            new OrpSeed("Vítkov", 49.7744, 17.7494),
            new OrpSeed("Vizovice", 49.2222, 17.8543),
            new OrpSeed("Vlašim", 49.7066, 14.8988),
            new OrpSeed("Vodňany", 49.1478, 14.1751),
            new OrpSeed("Votice", 49.6401, 14.6385),
            new OrpSeed("Vrchlabí", 50.6270, 15.6094),
            new OrpSeed("Vsetín", 49.3387, 17.9962),
            new OrpSeed("Vysoké Mýto", 49.9532, 16.1617),
            new OrpSeed("Vyškov", 49.2775, 16.9990),
            new OrpSeed("Zábřeh", 49.8825, 16.8722),
            new OrpSeed("Zlín", 49.2244, 17.6628),
            new OrpSeed("Znojmo", 48.8555, 16.0488),
            new OrpSeed("Žamberk", 50.0860, 16.4674),
            new OrpSeed("Žatec", 50.3272, 13.5458),
            new OrpSeed("Žďár n. Sázavou", 49.5626, 15.9392),
            new OrpSeed("Železný Brod", 50.6428, 15.2540),
            new OrpSeed("Židlochovice", 49.0357, 16.6189)
        };

        private static List<OrpAreaInfo> _cachedOrps;
        private static readonly object _initLock = new object();

        public static List<OrpAreaInfo> GetAllOrps()
        {
            lock (_initLock)
            {
                if (_cachedOrps != null)
                    return _cachedOrps;

                var byName = new Dictionary<string, OrpAreaInfo>();

                // Initialize all 206 ORP seeds so every official ORP is always represented
                for (int s = 0; s < OfficialOrpSeeds.Length; s++)
                {
                    var seed = OfficialOrpSeeds[s];
                    string reg = CzechData.FindRegionCode(seed.Lat, seed.Lon) ?? "STC";
                    var info = new OrpAreaInfo
                    {
                        Name = seed.Name,
                        RegionCode = reg,
                        CenterLat = seed.Lat,
                        CenterLon = seed.Lon
                    };
                    info.SamplePointsLatLon.Add(new PointF((float)seed.Lon, (float)seed.Lat));
                    byName[seed.Name] = info;
                }

                // Match each polygon ring in CzechGeoData.OrpPolygons and generate interior sample points
                for (int p = 0; p < CzechGeoData.OrpPolygons.Length; p++)
                {
                    float[] ring = CzechGeoData.OrpPolygons[p];
                    if (ring == null || ring.Length < 6) continue;

                    float minLon = float.MaxValue, maxLon = float.MinValue;
                    float minLat = float.MaxValue, maxLat = float.MinValue;
                    int n = ring.Length / 2;
                    double sumLon = 0, sumLat = 0;

                    for (int i = 0; i < n; i++)
                    {
                        float lon = ring[i * 2];
                        float lat = ring[i * 2 + 1];
                        if (lon < minLon) minLon = lon;
                        if (lon > maxLon) maxLon = lon;
                        if (lat < minLat) minLat = lat;
                        if (lat > maxLat) maxLat = lat;
                        sumLon += lon;
                        sumLat += lat;
                    }

                    double cLon = sumLon / n;
                    double cLat = sumLat / n;

                    // Find seed inside ring, or nearest seed to ring centroid
                    string matchedName = null;
                    double bestSeedD2 = double.MaxValue;
                    for (int s = 0; s < OfficialOrpSeeds.Length; s++)
                    {
                        var seed = OfficialOrpSeeds[s];
                        if (seed.Lon >= minLon && seed.Lon <= maxLon && seed.Lat >= minLat && seed.Lat <= maxLat)
                        {
                            if (PointInRing(seed.Lon, seed.Lat, ring))
                            {
                                double d2 = (seed.Lat - cLat) * (seed.Lat - cLat) + ((seed.Lon - cLon) * 0.65) * ((seed.Lon - cLon) * 0.65);
                                if (d2 < bestSeedD2)
                                {
                                    bestSeedD2 = d2;
                                    matchedName = seed.Name;
                                }
                            }
                        }
                    }

                    if (matchedName == null)
                    {
                        for (int s = 0; s < OfficialOrpSeeds.Length; s++)
                        {
                            var seed = OfficialOrpSeeds[s];
                            double dLat = seed.Lat - cLat;
                            double dLon = (seed.Lon - cLon) * 0.65;
                            double d2 = dLat * dLat + dLon * dLon;
                            if (d2 < bestSeedD2)
                            {
                                bestSeedD2 = d2;
                                matchedName = seed.Name;
                            }
                        }
                    }

                    OrpAreaInfo target = byName[matchedName];
                    // Sample a 3x3 sub-grid inside the polygon bounding box for smooth spatial field averaging
                    for (int iy = 1; iy <= 3; iy++)
                    {
                        float sLat = minLat + (maxLat - minLat) * (iy / 4.0f);
                        for (int ix = 1; ix <= 3; ix++)
                        {
                            float sLon = minLon + (maxLon - minLon) * (ix / 4.0f);
                            if (PointInRing(sLon, sLat, ring))
                                target.SamplePointsLatLon.Add(new PointF(sLon, sLat));
                        }
                    }
                }

                var list = new List<OrpAreaInfo>(byName.Values);
                list.Sort((a, b) =>
                {
                    int rc = string.Compare(a.RegionCode, b.RegionCode, StringComparison.OrdinalIgnoreCase);
                    if (rc != 0) return rc;
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                });

                _cachedOrps = list;
                return _cachedOrps;
            }
        }

        // Evaluates all 206 ORPs from the smoothed 4-NN IDW spatial grid field
        public static Dictionary<string, Dictionary<string, double?>> ComputeOrpTable(ForecastResult result, RegionAggregationMode mode)
        {
            var orps = GetAllOrps();
            var table = new Dictionary<string, Dictionary<string, double?>>(orps.Count);
            if (result == null)
                return table;

            bool hasGrid = result.GridPoints != null && result.GridPoints.Count > 0;

            foreach (var orp in orps)
            {
                var rowDict = new Dictionary<string, double?>();
                table[orp.Name] = rowDict;

                foreach (var m in result.Models)
                {
                    if (!hasGrid || m.Id.StartsWith("meteologix_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Regional-only model: fall back to regional value if available
                        double? regVal = null;
                        if (result.RegionModelSums.ContainsKey(orp.RegionCode))
                            result.RegionModelSums[orp.RegionCode].TryGetValue(m.Id, out regVal);
                        rowDict[m.Id] = regVal;
                        continue;
                    }

                    if (mode == RegionAggregationMode.PointValue)
                    {
                        rowDict[m.Id] = InterpolatePoint4NN(result.GridPoints, m.Id, result.SelectedTimeStepIndex, orp.CenterLat, orp.CenterLon);
                        continue;
                    }

                    double sum = 0;
                    double max = double.MinValue;
                    double min = double.MaxValue;
                    int cnt = 0;

                    for (int sp = 0; sp < orp.SamplePointsLatLon.Count; sp++)
                    {
                        var pt = orp.SamplePointsLatLon[sp];
                        double? v = InterpolatePoint4NN(result.GridPoints, m.Id, result.SelectedTimeStepIndex, pt.Y, pt.X);
                        if (v.HasValue)
                        {
                            sum += v.Value;
                            if (v.Value > max) max = v.Value;
                            if (v.Value < min) min = v.Value;
                            cnt++;
                        }
                    }

                    if (cnt == 0)
                    {
                        rowDict[m.Id] = null;
                    }
                    else if (mode == RegionAggregationMode.RegionMax)
                    {
                        rowDict[m.Id] = Math.Round(EnsembleStats.IsBelowThresholdWarning(result.Parameter) ? min : max, 2);
                    }
                    else
                    {
                        rowDict[m.Id] = Math.Round(sum / cnt, 2);
                    }
                }
            }

            return table;
        }

        private static double? InterpolatePoint4NN(List<GeoGridPoint> grid, string modelId, int stepIdx, double lat, double lon)
        {
            double d0 = double.MaxValue, d1 = double.MaxValue, d2 = double.MaxValue, d3 = double.MaxValue;
            double? v0 = null, v1 = null, v2 = null, v3 = null;

            for (int i = 0; i < grid.Count; i++)
            {
                double? gv = WeatherService.GetGridPointValue(grid[i], modelId, stepIdx);
                if (!gv.HasValue) continue;

                double dLat = lat - grid[i].Latitude;
                double dLon = (lon - grid[i].Longitude) * 0.65;
                double dist2 = dLat * dLat + dLon * dLon;

                if (dist2 < d0)
                {
                    d3 = d2; v3 = v2;
                    d2 = d1; v2 = v1;
                    d1 = d0; v1 = v0;
                    d0 = dist2; v0 = gv;
                }
                else if (dist2 < d1)
                {
                    d3 = d2; v3 = v2;
                    d2 = d1; v2 = v1;
                    d1 = dist2; v1 = gv;
                }
                else if (dist2 < d2)
                {
                    d3 = d2; v3 = v2;
                    d2 = dist2; v2 = gv;
                }
                else if (dist2 < d3)
                {
                    d3 = dist2; v3 = gv;
                }
            }

            if (!v0.HasValue) return null;
            if (d0 < 1e-5 || !v1.HasValue) return Math.Round(v0.Value, 2);

            double wSum = 0, valSum = 0;
            double[] ds = new double[] { d0, d1, d2, d3 };
            double?[] vs = new double?[] { v0, v1, v2, v3 };
            for (int k = 0; k < 4; k++)
            {
                if (!vs[k].HasValue) continue;
                double w = 1.0 / Math.Max(1e-5, ds[k]);
                wSum += w;
                valSum += w * vs[k].Value;
            }
            return wSum > 0 ? (double?)Math.Round(valSum / wSum, 2) : null;
        }

        private static bool PointInRing(double x, double y, float[] ring)
        {
            bool inside = false;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = ring[i * 2], yi = ring[i * 2 + 1];
                double xj = ring[j * 2], yj = ring[j * 2 + 1];
                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi))
                    inside = !inside;
            }
            return inside;
        }
    }
}
