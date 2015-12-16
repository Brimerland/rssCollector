using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Web;
using System.Xml;
using System.Xml.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApplication5
{
    public class Program
    {
        [XmlRoot("opml")]
        public class OPML
        {
            public OPMLBody body;
        }

        public class OPMLBody
        {
            [XmlElement("outline")]
            public List<OPMLOutline> outlines;
        }

        public class OPMLOutline
        {
            [XmlAttribute]
            public string text;
            [XmlAttribute]
            public string title;
            [XmlAttribute]
            public string type;
            [XmlAttribute]
            public string xmlUrl;
            [XmlAttribute]
            public string htmlUrl;
        }

        public interface IEntryFeed
        {
            List<Entry> GetEntries();
        }

        public class Entry
        {
            public string source;
            public string title;
            public string link;
            public DateTime date;
        }


        [XmlRoot("feed")]
        public class ATOMFeed
        {
            public string title;
            [XmlElement("entry")]
            public List<ATOMEntry> entries;
            public List<Entry> GetEntries()
            {
                var result = new List<Entry>();
                foreach (var item in entries)
                {
                    var entry = item.Entry;
                    entry.source = title;
                    result.Add(entry);
                }

                return result;
            }
        }

        public class ATOMEntry
        {
            public string title;
            public string link;
            public string summary;
            public string published;
            public string updated;
            public Entry Entry { get { return new Entry() { link = link, title = title, date = ConvertToDateTime(updated!=null ? updated : published) }; } }
        }

        [XmlRoot("rss")]
        public class RSS
        {
            [XmlElement("channel")]
            public List<RSSChannel> Channels;
            public List<Entry> GetEntries()
            {
                var result = new List<Entry>();
                foreach (var c in Channels)
                    foreach (var item in c.Items)
                    {
                        var entry = item.Entry;
                        entry.source = c.title;
                        result.Add(entry);
                    }

                return result;
            }
        }

        public class RSSChannel
        {
            public string title;
            public string link;
            public string description;
            
            [XmlElement("item")]
            public List<RSSItem> Items;
        }

        public class RSSItem
        {
            public string title;
            public string link;
            public string description;
            public string pubDate;            

            public Entry Entry { get { return new Entry() { link = link, title = title, date = ConvertToDateTime(pubDate) }; } }
        }

        static DateTime ConvertToDateTime(string s)
        {
            DateTime result = new DateTime();
            
            bool success = DateTime.TryParse(s, out result);
            if (!success)
            {
                // try stripping the timezone
                if (s!=null && s.Length > 3)
                {
                    s = s.Remove(s.Length-3,3);
                    success = DateTime.TryParse(s, out result);
                }

            }
            
            return result;
        }

        static void Main(string[] args)
        {
            OPML opml = null;
            {
                var x = new XmlSerializer(typeof(OPML));
                using (var stream = File.OpenRead("subscription.xml"))
                {
                    opml = (OPML)x.Deserialize(stream);
                }
            }

            var rssItems = new List<Entry>();

            if (opml != null)
            {
                var tasks = new List<Task>();
                foreach (var outline in opml.body.outlines)
                {
                    tasks.Add(Task.Run(async () =>
                      {
                          bool success = false;
                          
                          // get the content as string
                          string content = null;
                          try
                          {
                              var wr = HttpWebRequest.Create(outline.xmlUrl);

                              var response = await wr.GetResponseAsync();
                              using (var stream = response.GetResponseStream())
                              {
                                  var sr = new StreamReader(stream);
                                  content = await sr.ReadToEndAsync();
                              }
                              response.Close();
                          }
                          catch
                          { }


                          if (content != null)
                          {
                              Exception rssException = null;
                              // try to deserialize as RSS
                              try
                              {
                                  var x = new XmlSerializer(typeof(RSS));
                                  var rss = (RSS)x.Deserialize(new StringReader(content));
                                  lock (rssItems)
                                  {
                                      rssItems.AddRange(rss.GetEntries());
                                  }

                                  success = true;
                              }
                              catch (Exception e)
                              {
                                  rssException = e;
                              }

                              if (!success)
                              {
                                  // try to deserialize as RSS
                                  try
                                  {
                                      var x = new XmlSerializer(typeof(ATOMFeed),"http://www.w3.org/2005/Atom");
                                      var rss = (ATOMFeed)x.Deserialize(new StringReader(content));
                                      lock (rssItems)
                                      {
                                          rssItems.AddRange(rss.GetEntries());
                                      }

                                      success = true;
                                  }
                                  catch (Exception e)
                                  {
                                      Console.WriteLine("Defect: " + outline.title);
                                  }
                              }

                          }
                          else
                          {
                              Console.WriteLine("unable to get content for " + outline.title);
                          }
                      }
                    ));
                }

                Task.WaitAll(tasks.ToArray());
            }

            rssItems.Sort(new Comparison<Entry>((a, b) => { return b.date.CompareTo(a.date); }));
            using (var stream = File.OpenWrite("rss.txt"))
            {
                var sw = new StreamWriter(stream);
                foreach (var entry in rssItems)
                {
                    sw.WriteLine(entry.date + " --- " + entry.source + " --- " + entry.title);
                }
                sw.Close();
            }
        }
    }
}

