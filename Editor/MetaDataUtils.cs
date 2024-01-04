using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class MetaDataUtils
    {
        public record MetaData(string Email, string Author, Color Color, Task<Texture2D> Avatar)
        {
            public string FormattedAuthor => $"{Author} <{Email}>";
            public string FormattedAuthorColored => $"<color=#{ColorUtility.ToHtmlStringRGB(Color)}>{FormattedAuthor}</color>";
        }

        public const float AvatarSize = 46;

        static Dictionary<string, MetaData> users = new();
        static System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();

        public static MetaData GetUserData(string email, string name = null)
        {
            if (users.TryGetValue(email, out var userData))
                return userData;

            if (name == null)
                return null;

            Random.InitState((email + name).GetHashCode());
            userData = new MetaData(email, name, Random.ColorHSV(0f, 0.6f, 0.6f, 0.8f, 0.6f, 0.8f), LoadAvatar(email));
            users.Add(email, userData);
            return userData;
        }

        static async Task<Texture2D> LoadAvatar(string email)
        {
            string userHash = await Task.Run(() => Md5Hash(email.Trim().ToLower()));
            string avatarsDir = Path.Combine(Application.temporaryCachePath, "MRGitUI");
            string avatarPath = Path.Combine(avatarsDir, $"{userHash}.png");
            
            if (File.Exists(avatarPath))
            {
                var data = File.ReadAllBytes(avatarPath);
                var cachedImage = new Texture2D(2, 2);
                cachedImage.LoadImage(data);
                return cachedImage;
            }
            
            string url = $"https://www.gravatar.com/avatar/{userHash}?s={AvatarSize}&d=retro";
            var avatar = await DownloadTextureAsync(url);
            if (!Directory.Exists(avatarsDir))
                Directory.CreateDirectory(avatarsDir);
            File.WriteAllBytes(avatarPath, avatar.EncodeToPNG());
            return avatar;
        }
        
        private static async Task<Texture2D> DownloadTextureAsync(string url)
        {
            using HttpClient client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(url);
            var texture = new Texture2D(2, 2);
            texture.LoadImage(bytes);
            return texture;
        }

        static string Md5Hash(string input) => string.Join(null, md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)).Select(x => x.ToString("X2"))).ToLower();
    }
}
