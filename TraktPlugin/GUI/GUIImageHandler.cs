﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using MediaPortal.GUI.Library;
using MediaPortal.Util;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    [Flags]
    public enum MainOverlayImage
    {
        None = 0,
        Watchlist = 1,
        Seenit = 2,
        Library = 4,
    }

    /// <summary>
    /// Support both Advanced and Simple rating overlays
    /// Order of enum values are important!
    /// </summary>
    public enum RatingOverlayImage
    {
        None,
        Heart1,
        Heart2,
        Heart3,
        Heart4,
        Heart5,
        Heart6,
        Heart7,
        Heart8,
        Heart9,
        Heart10,
        Love,
        Hate
    }

    public static class GUIImageHandler
    {
        /// <summary>
        /// Get a overlay for images that represent a users Advanced Rating score
        /// </summary>
        /// <param name="rating">the movie, show or episodes advanced rating (backwards compatible with simple ratings)</param>
        internal static RatingOverlayImage GetRatingOverlay(int rating)
        {
            RatingOverlayImage ratingOverlay = RatingOverlayImage.None;

            if (!TraktSettings.ShowAdvancedRatingsDialog)
            {
                if (rating > 5)
                    ratingOverlay = RatingOverlayImage.Love;
                else if (rating >= 1)
                    ratingOverlay = RatingOverlayImage.Hate;
            }
            else
            {
                // do extra check to confirm new skin images exist
                // if not fall back to basic overlays
                if (!File.Exists(GUIGraphicsContext.Skin + string.Format(@"\Media\traktHeart{0}.png", rating)))
                {
                    if (rating > 5)
                        ratingOverlay = RatingOverlayImage.Love;
                    else if (rating >= 1)
                        ratingOverlay = RatingOverlayImage.Hate;
                }
                else
                    ratingOverlay = (RatingOverlayImage)rating;
            }

            return ratingOverlay;
        }

        /// <summary>
        /// Returns a user rating overlay to display on a user shout
        /// </summary>
        internal static RatingOverlayImage GetRatingOverlay(TraktShout.UserRating userRating)
        {
            RatingOverlayImage ratingOverlay = RatingOverlayImage.None;

            if (userRating.AdvancedRating == 0 && userRating.Rating != "false")
            {
                if (userRating.Rating == "love") ratingOverlay = RatingOverlayImage.Love;
                if (userRating.Rating == "hate") ratingOverlay = RatingOverlayImage.Hate;
            }
            else if (userRating.AdvancedRating > 0)
            {
                // do extra check to confirm new skin images exist
                // if not fall back to basic overlays
                if (!File.Exists(GUIGraphicsContext.Skin + string.Format(@"\Media\traktHeart{0}.png", userRating.AdvancedRating)))
                {
                    if (userRating.AdvancedRating > 5)
                        ratingOverlay = RatingOverlayImage.Love;
                    else if (userRating.AdvancedRating >= 1)
                        ratingOverlay = RatingOverlayImage.Hate;
                }
                else
                    ratingOverlay = (RatingOverlayImage)userRating.AdvancedRating;
            }

            return ratingOverlay;
        }

        /// <summary>
        /// Download an image if it does not exist locally
        /// </summary>
        /// <param name="url">Online URL of image to download</param>
        /// <param name="localFile">Local filename to save image</param>
        /// <returns>true if image downloads successfully or loads from disk successfully</returns>
        public static bool DownloadImage(string url, string localFile)
        {
            WebClient webClient = new WebClient();
            webClient.Headers.Add("user-agent", TraktSettings.UserAgent);

            // Ignore Image placeholders (series/movies with no artwork)
            // use skins default images instead
            if (url.Contains("poster-small") || url.Contains("fanart-summary")) return false;
            if (url.Contains("poster-dark") || url.Contains("fanart-dark")) return false;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localFile));
                if (!File.Exists(localFile))
                {
                    TraktLogger.Debug("Downloading new image from: {0}", url);
                    webClient.DownloadFile(url, localFile);
                }
                return true;
            }
            catch (Exception)
            {
                TraktLogger.Warning("Image download failed from '{0}' to '{1}'", url, localFile);
                try { if (File.Exists(localFile)) File.Delete(localFile); } catch { }
                return false;
            }
        }

        public static void LoadFanart(ImageSwapper backdrop, string filename)
        {
            // Dont activate and load if user does not want to download fanart
            if (!TraktSettings.DownloadFanart)
            {
                if (backdrop.Active) backdrop.Active = false;
                return;
            }
            
            // Activate Backdrop in Image Swapper
            if (!backdrop.Active) backdrop.Active = true;

            if (string.IsNullOrEmpty(filename) || filename.Contains("fanart-summary") || filename.Contains("fanart-dark") || !File.Exists(filename))
                filename = string.Empty;

            // Assign Fanart filename to Image Loader
            // Will display fanart in backdrop or reset to default background
            backdrop.Filename = filename;
        }

        /// <summary>
        /// Loads an image FAST from file
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Image LoadImage(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            
            Image img = null;

            try
            {
                img = ImageFast.FromFile(file);
            }
            catch
            {
                // Most likely a Zero Byte file but not always
                TraktLogger.Warning("Fast loading of texture {0} failed - trying safe fallback now", file);
                try { img = Image.FromFile(file); } catch { }
            }

            return img;
        }

        /// <summary>
        /// Gets a MediaPortal texture identifier from filename
        /// </summary>
        /// <param name="filename">Filename to generate texture</param>
        /// <returns>MediaPortal texture identifier</returns>
        public static string GetTextureIdentFromFile(string filename)
        {
            return GetTextureIdentFromFile(filename, string.Empty);
        }
        
        public static string GetTextureIdentFromFile(string filename, string suffix)
        {
            return "[Trakt:" + (filename + suffix).GetHashCode() + "]";
        }

        public static Bitmap DrawOverlayOnPoster(string origPoster, MainOverlayImage mainType, RatingOverlayImage ratingType)
        {
            return DrawOverlayOnPoster(origPoster, mainType, ratingType, new Size());
        }

        /// <summary>
        /// Draws a trakt overlay, library/seen/watchlist icon on a poster
        /// This is done in memory and wont touch the existing file
        /// </summary>
        /// <param name="origPoster">Filename of the untouched poster</param>
        /// <param name="type">Overlay type enum</param>
        /// <param name="size">Size of returned image</param>
        /// <returns>An image with overlay added to poster</returns>
        public static Bitmap DrawOverlayOnPoster(string origPoster, MainOverlayImage mainType, RatingOverlayImage ratingType, Size size)
        {
            Image image = GUIImageHandler.LoadImage(origPoster);
            if (image == null) return null;

            Bitmap poster = size.IsEmpty ? new Bitmap(image) : new Bitmap(image, size);
            Graphics gph = Graphics.FromImage(poster);

            string mainOverlayImage = GUIGraphicsContext.Skin + string.Format(@"\Media\trakt{0}.png", mainType.ToString().Replace(", ", string.Empty));
            if (mainType != MainOverlayImage.None && File.Exists(mainOverlayImage))
            {
                Bitmap newPoster = new Bitmap(GUIImageHandler.LoadImage(mainOverlayImage));
                gph.DrawImage(newPoster, TraktSkinSettings.PosterMainOverlayPosX, TraktSkinSettings.PosterMainOverlayPosY);
            }

            string ratingOverlayImage = GUIGraphicsContext.Skin + string.Format(@"\Media\trakt{0}.png", Enum.GetName(typeof(RatingOverlayImage), ratingType));
            if (ratingType != RatingOverlayImage.None && File.Exists(ratingOverlayImage))
            {
                Bitmap newPoster = new Bitmap(GUIImageHandler.LoadImage(ratingOverlayImage));
                gph.DrawImage(newPoster, TraktSkinSettings.PosterRatingOverlayPosX, TraktSkinSettings.PosterRatingOverlayPosY);
            }

            gph.Dispose();
            return poster;
        }

        /// <summary>
        /// Draws a trakt overlay, library/seen/watchlist icon on a episode thumb
        /// This is done in memory and wont touch the existing file
        /// </summary>
        /// <param name="origThumb">Filename of the untouched episode thumb</param>
        /// <param name="type">Overlay type enum</param>
        /// <param name="size">Size of returned image</param>
        /// <returns>An image with overlay added to episode thumb</returns>
        public static Bitmap DrawOverlayOnEpisodeThumb(string origThumb, MainOverlayImage mainType, RatingOverlayImage ratingType, Size size)
        {
            Image image = GUIImageHandler.LoadImage(origThumb);
            if (image == null) return null;

            Bitmap thumb = new Bitmap(image, size);
            Graphics gph = Graphics.FromImage(thumb);

            string mainOverlayImage = GUIGraphicsContext.Skin + string.Format(@"\Media\trakt{0}.png", mainType.ToString().Replace(", ", string.Empty));
            if (mainType != MainOverlayImage.None && File.Exists(mainOverlayImage))
            {
                Bitmap newThumb = new Bitmap(GUIImageHandler.LoadImage(mainOverlayImage));
                gph.DrawImage(newThumb, TraktSkinSettings.EpisodeThumbMainOverlayPosX, TraktSkinSettings.EpisodeThumbMainOverlayPosY);
            }

            string ratingOverlayImage = GUIGraphicsContext.Skin + string.Format(@"\Media\trakt{0}.png", Enum.GetName(typeof(RatingOverlayImage), ratingType));
            if (ratingType != RatingOverlayImage.None && File.Exists(ratingOverlayImage))
            {
                Bitmap newThumb = new Bitmap(GUIImageHandler.LoadImage(ratingOverlayImage));
                gph.DrawImage(newThumb, TraktSkinSettings.EpisodeThumbRatingOverlayPosX, TraktSkinSettings.EpisodeThumbRatingOverlayPosY);
            }

            gph.Dispose();
            return thumb;
        }

        /// <summary>
        /// Draws a trakt overlay, rating icon on a poster
        /// This is done in memory and wont touch the existing file
        /// </summary>
        /// <param name="origPoster">Filename of the untouched avatar</param>
        /// <param name="type">Overlay type enum</param>
        /// <param name="size">Size of returned image</param>
        /// <returns>An image with overlay added to avatar</returns>
        public static Bitmap DrawOverlayOnAvatar(string origAvartar, RatingOverlayImage ratingType, Size size)
        {
            Image image = GUIImageHandler.LoadImage(origAvartar);
            if (image == null) return null;

            Bitmap avatar = size.IsEmpty ? new Bitmap(image) : new Bitmap(image, size);
            Graphics gph = Graphics.FromImage(avatar);

            string ratingOverlayImage = GUIGraphicsContext.Skin + string.Format(@"\Media\trakt{0}.png", Enum.GetName(typeof(RatingOverlayImage), ratingType));
            if (ratingType != RatingOverlayImage.None && File.Exists(ratingOverlayImage))
            {
                Bitmap newAvatar = new Bitmap(GUIImageHandler.LoadImage(ratingOverlayImage));
                gph.DrawImage(newAvatar, TraktSkinSettings.AvatarRatingOverlayPosX, TraktSkinSettings.AvatarRatingOverlayPosY);
            }

            gph.Dispose();
            return avatar;
        }
    }
}
