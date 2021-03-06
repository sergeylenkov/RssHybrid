﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Web.Syndication;

namespace RSSHybrid
{
    public static class DataAccess
    {
        private static string GetConnectionString()
        {
            string path = ApplicationData.Current.LocalFolder.Path + "\\Database.sqlite";
            return "Data Source=" + path + ";Version=3;";
        }

        public static void InitializeDatabase()
        {
            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string feedsCommand = "CREATE TABLE IF NOT EXISTS feeds (id INTEGER PRIMARY KEY, rss TEXT, link TEXT, title TEXT, description TEXT, image TEXT, active INTEGER, status TEXT, last_update TEXT, deleted INTEGER)";
                string entriesCommand = "CREATE TABLE IF NOT EXISTS entries (id INTEGER PRIMARY KEY, feed_id INTEGER, guid TEXT, link TEXT, title TEXT, description TEXT, date TEXT, read INTEGER, viewed INTEGER, favorite INTEGER)";

                SQLiteCommand command = new SQLiteCommand(feedsCommand, db);
                command.ExecuteNonQuery();

                command = new SQLiteCommand(entriesCommand, db);
                command.ExecuteNonQuery();

                db.Close();
            }
        }

        public static string GetFeeds()
        {
            string json = "";

            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string sql = "SELECT f.*, COUNT(e.id) AS total, 0 AS count FROM feeds f LEFT JOIN entries e ON e.feed_id = f.id WHERE f.deleted = 0 GROUP BY f.id";
                SQLiteCommand command = new SQLiteCommand(sql, db);

                SQLiteDataReader reader = command.ExecuteReader();
                
                json = JsonConvert.SerializeObject(Serialize(reader), Formatting.Indented);

                db.Close();
            }

            return json;
        }

        public static int GetTotalCount()
        {
            int result = 0;

            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string sql = "SELECT COUNT(*) FROM entries";
                SQLiteCommand command = new SQLiteCommand(sql, db);

                result = Int32.Parse(command.ExecuteScalar().ToString());

                db.Close();
            }

            return result;
        }

        public static int GetUnviewedCount()
        {
            int result = 0;

            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string sql = "SELECT COUNT(*) FROM entries WHERE viewed = @viewed";

                SQLiteCommand command = new SQLiteCommand(sql, db);
                command.Parameters.Add(new SQLiteParameter("@viewed", false));

                result = Int32.Parse(command.ExecuteScalar().ToString());

                db.Close();
            }

            return result;
        }

        public static string GetAllNews(int offset, int limit)
        {
            string json = "";

            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string sql = "SELECT * FROM entries ORDER BY id DESC LIMIT @limit OFFSET @offset";
                SQLiteCommand command = new SQLiteCommand(sql, db);

                var limitParam = new SQLiteParameter("@limit", limit);
                var offsetParam = new SQLiteParameter("@offset", offset);

                command.Parameters.Add(limitParam);
                command.Parameters.Add(offsetParam);

                SQLiteDataReader reader = command.ExecuteReader();

                json = JsonConvert.SerializeObject(Serialize(reader), Formatting.Indented);
                db.Close();
            }

            return json;
        }

        public static string GetUnviewedNews()
        {
            string json = "";

            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string sql = "SELECT * FROM entries WHERE viewed = @viewed ORDER BY id DESC";

                SQLiteCommand command = new SQLiteCommand(sql, db);
                command.Parameters.Add(new SQLiteParameter("@viewed", false));

                SQLiteDataReader reader = command.ExecuteReader();

                json = JsonConvert.SerializeObject(Serialize(reader), Formatting.Indented);

                db.Close();
            }

            return json;
        }

        public static async Task UpdateFeeds()
        {
            await Task.Run(async () => {
                using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
                {
                    db.Open();
                    
                    string sql = "SELECT id, rss FROM feeds";
                    SQLiteCommand command = new SQLiteCommand(sql, db);

                    SQLiteDataReader reader = command.ExecuteReader();

                    string existsSql = "SELECT id FROM entries WHERE feed_id = @feedId AND guid = @guid";
                    SQLiteCommand existsCommand = new SQLiteCommand(existsSql, db);

                    existsCommand.Parameters.Add("@feedId", DbType.Int32);
                    existsCommand.Parameters.Add("@guid", DbType.String);

                    while (reader.Read())
                    {
                        var feedId = reader.GetInt32(0);
                        var feedUrl = reader.GetString(1);

                        var uri = new Uri(feedUrl);

                        SyndicationClient client = new SyndicationClient();
                        client.BypassCacheOnRetrieve = true;

                        try
                        {
                            SyndicationFeed feed = await client.RetrieveFeedAsync(uri);

                            foreach (var item in feed.Items)
                            {
                                string itemId = "";
                                string itemLink = "";

                                if (item.Links != null)
                                {
                                    itemLink = item.Links[0].Uri.ToString();
                                }

                                if (item.Id.Length > 0)
                                {
                                    itemId = item.Id;
                                }
                                else
                                {
                                    itemId = itemLink;
                                }
                                
                                existsCommand.Parameters["@feedId"].Value = feedId;
                                existsCommand.Parameters["@guid"].Value = itemId;

                                var column = existsCommand.ExecuteScalar();

                                if (column == null)
                                {
                                    Debug.WriteLine("Insert new {0}", (object)itemId);
                                    string insertSql = "INSERT INTO entries(feed_id, guid, link, title, description, read, viewed, favorite, date) VALUES(@feedId, @guid, @link, @title, @description, @read, @viewed, @favorite, @date)";
                                    SQLiteCommand insertCommand = new SQLiteCommand(insertSql, db);

                                    insertCommand.Parameters.Add(new SQLiteParameter("@feedId", feedId));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@guid", itemId));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@link", itemLink));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@title", item.Title.Text));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@description", item.Summary.Text));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@read", false));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@viewed", false));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@favorite", false));
                                    insertCommand.Parameters.Add(new SQLiteParameter("@date", item.PublishedDate));

                                    insertCommand.ExecuteNonQuery();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.ToString());
                        }
                    }

                    db.Close();
                }
            });
        }

        public static void MarkAsViewed(string ids)
        {
            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string updateSql = String.Format("UPDATE entries SET viewed = @viewed WHERE id IN({0})", (object)ids);
                SQLiteCommand updateCommand = new SQLiteCommand(updateSql, db);

                updateCommand.Parameters.Add(new SQLiteParameter("@viewed", true));
                updateCommand.ExecuteNonQuery();

                db.Close();
            }
        }

        public static Dictionary<string, string> GetLastUnviewedEntry()
        {
            var result = new Dictionary<string, string>();

            result.Add("feed", "");
            result.Add("title", "");
            result.Add("description", "");

            using (SQLiteConnection db = new SQLiteConnection(GetConnectionString()))
            {
                db.Open();

                string sql = "SELECT f.title, e.title, e.description FROM entries e, feeds f WHERE e.viewed = 0 AND e.feed_id = f.id ORDER BY e.id DESC LIMIT 1";
                SQLiteCommand command = new SQLiteCommand(sql, db);

                SQLiteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    result["feed"] = reader.GetString(0);
                    result["title"] = reader.GetString(1);
                    result["description"] = reader.GetString(2);
                }

                db.Close();
            }

            return result;
        }

        public static IEnumerable<Dictionary<string, object>> Serialize(SQLiteDataReader reader)
        {
            var results = new List<Dictionary<string, object>>();
            var cols = new List<string>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                cols.Add(reader.GetName(i));
            }

            while (reader.Read())
            {
                results.Add(SerializeRow(cols, reader));
            }

            return results;
        }

        private static Dictionary<string, object> SerializeRow(IEnumerable<string> cols, SQLiteDataReader reader)
        {
            var result = new Dictionary<string, object>();

            foreach (var col in cols)
            {
                result.Add(col, reader[col]);
            }

            return result;
        }
    }
}
