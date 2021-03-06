﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using Action = MediaPortal.GUI.Library.Action;
using MediaPortal.Util;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    public class GUIWatchListEpisodes : GUIWindow
    {
        #region Skin Controls

        [SkinControl(2)]
        protected GUIButtonControl layoutButton = null;

        [SkinControl(50)]
        protected GUIFacadeControl Facade = null;

        [SkinControlAttribute(60)]
        protected GUIImage FanartBackground = null;

        [SkinControlAttribute(61)]
        protected GUIImage FanartBackground2 = null;

        [SkinControlAttribute(62)]
        protected GUIImage loadingImage = null;

        #endregion

        #region Enums

        enum ContextMenuItem
        {
            RemoveFromWatchList,
            AddToList,
            Trailers,
            Shouts,
            ChangeLayout,
            SearchWithMpNZB,
            SearchTorrent
        }

        #endregion

        #region Constructor

        public GUIWatchListEpisodes()
        {
            backdrop = new ImageSwapper();
            backdrop.PropertyOne = "#Trakt.WatchListEpisodes.Fanart.1";
            backdrop.PropertyTwo = "#Trakt.WatchListEpisodes.Fanart.2";
        }

        #endregion

        #region Private Variables

        bool StopDownload { get; set; }
        bool RemovingWatchListItem { get; set; }
        private Layout CurrentLayout { get; set; }
        int PreviousSelectedIndex { get; set; }
        ImageSwapper backdrop;
        DateTime LastRequest = new DateTime();
        Dictionary<string, IEnumerable<TraktWatchListEpisode>> userWatchList = new Dictionary<string, IEnumerable<TraktWatchListEpisode>>();

        IEnumerable<TraktWatchListEpisode> WatchListEpisodes
        {
            get
            {
                if (!userWatchList.Keys.Contains(CurrentUser) || LastRequest < DateTime.UtcNow.Subtract(new TimeSpan(0, TraktSettings.WebRequestCacheMinutes, 0)))
                {
                    _WatchListEpisodes = TraktAPI.TraktAPI.GetWatchListEpisodes(CurrentUser);
                    if (userWatchList.Keys.Contains(CurrentUser)) userWatchList.Remove(CurrentUser);
                    userWatchList.Add(CurrentUser, _WatchListEpisodes);
                    LastRequest = DateTime.UtcNow;
                    PreviousSelectedIndex = 0;
                }
                return userWatchList[CurrentUser];
            }
        }
        IEnumerable<TraktWatchListEpisode> _WatchListEpisodes = null;

        #endregion

        #region Public Properties

        public static string CurrentUser { get; set; }

        #endregion

        #region Base Overrides

        public override int GetID
        {
            get
            {
                return 87269;
            }
        }

        public override bool Init()
        {
            return Load(GUIGraphicsContext.Skin + @"\Trakt.WatchList.Episodes.xml");
        }

        protected override void OnPageLoad()
        {
            base.OnPageLoad();

            // Clear GUI Properties
            ClearProperties();

            // Requires Login
            if (!GUICommon.CheckLogin()) return;
          
            // Init Properties
            InitProperties();

            // Load WatchList Episodes
            LoadWatchListEpisodes();
        }

        protected override void OnPageDestroy(int new_windowId)
        {
            StopDownload = true;
            PreviousSelectedIndex = Facade.SelectedListItemIndex;
            ClearProperties();

            // save current layout
            TraktSettings.WatchListEpisodesDefaultLayout = (int)CurrentLayout;

            base.OnPageDestroy(new_windowId);
        }

        protected override void OnClicked(int controlId, GUIControl control, Action.ActionType actionType)
        {
            // wait for any background action to finish
            if (GUIBackgroundTask.Instance.IsBusy) return;

            switch (controlId)
            {
                // Facade
                case (50):
                    if (actionType == Action.ActionType.ACTION_SELECT_ITEM)
                    {
                        CheckAndPlayEpisode();
                    }
                    break;

                // Layout Button
                case (2):
                    CurrentLayout = GUICommon.ShowLayoutMenu(CurrentLayout);
                    break;

                default:
                    break;
            }
            base.OnClicked(controlId, control, actionType);
        }

        public override void OnAction(Action action)
        {
            switch (action.wID)
            {
                case Action.ActionType.ACTION_PREVIOUS_MENU:
                    // restore current user
                    CurrentUser = TraktSettings.Username;
                    base.OnAction(action);
                    break;
                case Action.ActionType.ACTION_PLAY:
                case Action.ActionType.ACTION_MUSIC_PLAY:
                    CheckAndPlayEpisode();
                    break;
                default:
                    base.OnAction(action);
                    break;
            }
        }

        protected override void OnShowContextMenu()
        {
            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            var item = (KeyValuePair<TraktShow, TraktWatchListEpisode.Episode>)selectedItem.TVTag;
            var selectedSeries = item.Key;
            var selectedEpisode = item.Value;

            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem listItem = null;

            if (CurrentUser == TraktSettings.Username)
            {
                listItem = new GUIListItem(Translation.RemoveFromWatchList);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.RemoveFromWatchList;
            }

            if (TraktHelper.IsOnlineVideosAvailableAndEnabled)
            {
                listItem = new GUIListItem(Translation.Trailers);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.Trailers;
            }

            // Add to Custom List
            listItem = new GUIListItem(Translation.AddToList + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.AddToList;

            // Shouts
            listItem = new GUIListItem(Translation.Shouts + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Shouts;
            
            // Change Layout
            listItem = new GUIListItem(Translation.ChangeLayout);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.ChangeLayout;

            if (TraktHelper.IsMpNZBAvailableAndEnabled)
            {
                // Search for show with mpNZB
                listItem = new GUIListItem(Translation.SearchWithMpNZB);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.SearchWithMpNZB;
            }

            if (TraktHelper.IsMyTorrentsAvailableAndEnabled)
            {
                // Search for show with MyTorrents
                listItem = new GUIListItem(Translation.SearchTorrent);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.SearchTorrent;
            }

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)ContextMenuItem.RemoveFromWatchList):
                    RemovingWatchListItem = true;
                    PreviousSelectedIndex = this.Facade.SelectedListItemIndex;
                    RemoveEpisodeFromWatchList(item);
                    if (this.Facade.Count >= 1)
                    {
                        // remove from list
                        _WatchListEpisodes = null;
                        userWatchList.Remove(CurrentUser);
                        LoadWatchListEpisodes();
                    }
                    else
                    {
                        // no more shows left
                        ClearProperties();
                        GUIControl.ClearControl(GetID, Facade.GetID);
                        _WatchListEpisodes = null;
                        userWatchList.Remove(CurrentUser);
                        // notify and exit
                        GUIUtils.ShowNotifyDialog(GUIUtils.PluginName(), Translation.NoShowWatchList);
                        GUIWindowManager.ShowPreviousWindow();
                        return;
                    }
                    break;

                case ((int)ContextMenuItem.AddToList):
                    TraktHelper.AddRemoveEpisodeInUserList(selectedSeries.Title, selectedSeries.Year.ToString(), selectedEpisode.Season.ToString(), selectedEpisode.Number.ToString(), selectedSeries.Tvdb, false);
                    break;

                case ((int)ContextMenuItem.Trailers):
                    GUICommon.ShowTVShowTrailersMenu(selectedSeries, selectedEpisode);
                    break;

                case ((int)ContextMenuItem.Shouts):
                    GUIShouts.ShoutType = GUIShouts.ShoutTypeEnum.episode;
                    GUIShouts.EpisodeInfo = new EpisodeShout
                    { 
                        TVDbId = selectedSeries.Tvdb, 
                        IMDbId = selectedSeries.Imdb, 
                        Title = selectedSeries.Title, 
                        SeasonIdx = selectedEpisode.Season.ToString(), 
                        EpisodeIdx = selectedEpisode.Number.ToString()
                    };
                    GUIShouts.Fanart = selectedSeries.Images.FanartImageFilename;
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Shouts);
                    break;

                case ((int)ContextMenuItem.ChangeLayout):
                    CurrentLayout = GUICommon.ShowLayoutMenu(CurrentLayout);
                    break;

                case ((int)ContextMenuItem.SearchWithMpNZB):
                    string loadingParam = string.Format("search:{0} S{1}E{2}", selectedSeries.Title, selectedEpisode.Season.ToString("D2"), selectedEpisode.Number.ToString("D2"));
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MpNZB, loadingParam);
                    break;

                case ((int)ContextMenuItem.SearchTorrent):
                    string loadPar = string.Format("{0} S{1}E{2}", selectedSeries.Title, selectedEpisode.Season.ToString("D2"), selectedEpisode.Number.ToString("D2"));
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyTorrents, loadPar);
                    break;

                default:
                    break;
            }

            base.OnShowContextMenu();
        }

        #endregion

        #region Private Methods

        private void CheckAndPlayEpisode()
        {
            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            var item = (KeyValuePair<TraktShow, TraktWatchListEpisode.Episode>)selectedItem.TVTag;
            var selectedSeries = item.Key;
            var selectedEpisode = item.Value;

            GUICommon.CheckAndPlayEpisode(selectedSeries, selectedEpisode);
        }

        private TraktEpisodeSync CreateSyncData(KeyValuePair<TraktShow, TraktWatchListEpisode.Episode> item)
        {
            var series = item.Key;
            var episode = item.Value;

            List<TraktEpisodeSync.Episode> episodes = new List<TraktEpisodeSync.Episode>();

            TraktEpisodeSync.Episode ep = new TraktEpisodeSync.Episode
            {
                EpisodeIndex = episode.Number.ToString(),
                SeasonIndex = episode.Season.ToString()                
            };
            episodes.Add(ep);

            TraktEpisodeSync syncData = new TraktEpisodeSync
            {
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password,
                SeriesID = series.Tvdb,
                Title = series.Title,
                Year = series.Year.ToString(),
                EpisodeList = episodes
            };

            return syncData;
        }

        private void RemoveEpisodeFromWatchList(KeyValuePair<TraktShow, TraktWatchListEpisode.Episode> item)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncEpisodeWatchList(CreateSyncData((KeyValuePair<TraktShow, TraktWatchListEpisode.Episode>)obj), TraktSyncModes.unwatchlist);
                RemovingWatchListItem = false;
            })
            {
                IsBackground = true,
                Name = "RemoveWatchList"
            };

            syncThread.Start(item);
        }

        private void LoadWatchListEpisodes()
        {
            GUIUtils.SetProperty("#Trakt.Items", string.Empty);

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                // wait until watched item has been removed or timesout (10secs)
                while (RemovingWatchListItem) Thread.Sleep(500);
                return WatchListEpisodes;
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    IEnumerable<TraktWatchListEpisode> shows = result as IEnumerable<TraktWatchListEpisode>;
                    SendWatchListEpisodesToFacade(shows);
                }
            }, Translation.GettingWatchListEpisodes, true);
        }

        private void SendWatchListEpisodesToFacade(IEnumerable<TraktWatchListEpisode> shows)
        {
            // clear facade
            GUIControl.ClearControl(GetID, Facade.GetID);

            if (shows.Count() == 0)
            {
                GUIUtils.ShowNotifyDialog(GUIUtils.PluginName(), string.Format(Translation.NoEpisodeWatchList, CurrentUser));
                CurrentUser = TraktSettings.Username;
                GUIWindowManager.ShowPreviousWindow();
                return;
            }

            int itemCount = 0;
            List<TraktImage> showImages = new List<TraktImage>();

            // Add each show and underlying episodes
            // Should we do facade levels (Series,Season,Episodes)?
            foreach (var show in shows)
            {
                foreach (var episode in show.Episodes)
                {
                    string itemLabel = string.Format("{0} - {1}x{2}{3}", show.Title, episode.Season.ToString(), episode.Number.ToString(), string.IsNullOrEmpty(episode.Title) ? string.Empty : " - " + episode.Title);

                    GUITraktWatchListEpisodeListItem item = new GUITraktWatchListEpisodeListItem(itemLabel);

                    // add image for download
                    TraktImage images = new TraktImage
                    {
                        EpisodeImages = episode.Images,
                        ShowImages = show.Images
                    };
                    showImages.Add(images);

                    item.Label2 = episode.FirstAired.FromEpoch().ToShortDateString();
                    item.TVTag = new KeyValuePair<TraktShow, TraktWatchListEpisode.Episode>(show, episode);
                    item.Item = images;
                    item.ItemId = Int32.MaxValue - itemCount;
                    item.IconImage = "defaultTraktEpisode.png";
                    item.IconImageBig = "defaultTraktEpisodeBig.png";
                    item.ThumbnailImage = "defaultTraktEpisodeBig.png";
                    item.OnItemSelected += OnEpisodeSelected;
                    Utils.SetDefaultIcons(item);
                    Facade.Add(item);
                    itemCount++;
                }
            }

            // Set Facade Layout
            Facade.SetCurrentLayout(Enum.GetName(typeof(Layout), CurrentLayout));
            GUIControl.FocusControl(GetID, Facade.GetID);

            if (PreviousSelectedIndex >= itemCount)
                Facade.SelectIndex(PreviousSelectedIndex - 1);
            else
                Facade.SelectIndex(PreviousSelectedIndex);

            // set facade properties
            GUIUtils.SetProperty("#itemcount", itemCount.ToString());
            GUIUtils.SetProperty("#Trakt.Items", string.Format("{0} {1}", itemCount.ToString(), itemCount > 1 ? Translation.Episodes : Translation.Episode));

            // Download episode images Async and set to facade
            GetImages(showImages);
        }

        private void InitProperties()
        {
            // Fanart
            backdrop.GUIImageOne = FanartBackground;
            backdrop.GUIImageTwo = FanartBackground2;
            backdrop.LoadingImage = loadingImage;

            RemovingWatchListItem = false;

            // load Watch list for user
            if (string.IsNullOrEmpty(CurrentUser)) CurrentUser = TraktSettings.Username;
            GUICommon.SetProperty("#Trakt.WatchList.CurrentUser", CurrentUser);

            // load last layout
            CurrentLayout = (Layout)TraktSettings.WatchListEpisodesDefaultLayout;
            // update button label
            GUIControl.SetControlLabel(GetID, layoutButton.GetID, GUICommon.GetLayoutTranslation(CurrentLayout));
        }

        private void ClearProperties()
        {
            GUICommon.SetProperty("#Trakt.Episode.WatchList.Inserted", string.Empty);

            GUICommon.ClearShowProperties();
            GUICommon.ClearEpisodeProperties();
        }

        private void PublishEpisodeSkinProperties(KeyValuePair<TraktShow, TraktWatchListEpisode.Episode> e)
        {
            var show = e.Key;
            var episode = e.Value;

            GUICommon.SetProperty("#Trakt.Episode.WatchList.Inserted", episode.Inserted.FromEpoch().ToShortDateString());

            GUICommon.SetShowProperties(show);
            GUICommon.SetEpisodeProperties(episode);
        }

        private void OnEpisodeSelected(GUIListItem item, GUIControl parent)
        {
            var episode = (KeyValuePair<TraktShow, TraktWatchListEpisode.Episode>)item.TVTag;
            PublishEpisodeSkinProperties(episode);
            GUIImageHandler.LoadFanart(backdrop, episode.Key.Images.FanartImageFilename);
        }

        private void GetImages(List<TraktImage> itemsWithThumbs)
        {
            StopDownload = false;

            // split the downloads in 5+ groups and do multithreaded downloading
            int groupSize = (int)Math.Max(1, Math.Floor((double)itemsWithThumbs.Count / 5));
            int groups = (int)Math.Ceiling((double)itemsWithThumbs.Count() / groupSize);

            for (int i = 0; i < groups; i++)
            {
                List<TraktImage> groupList = new List<TraktImage>();
                for (int j = groupSize * i; j < groupSize * i + (groupSize * (i + 1) > itemsWithThumbs.Count ? itemsWithThumbs.Count - groupSize * i : groupSize); j++)
                {
                    groupList.Add(itemsWithThumbs[j]);
                }

                new Thread(delegate(object o)
                {
                    List<TraktImage> items = (List<TraktImage>)o;
                    foreach (TraktImage item in items)
                    {
                        #region Episode Image
                        // stop download if we have exited window
                        if (StopDownload) break;

                        string remoteThumb = item.EpisodeImages.Screen;
                        string localThumb = item.EpisodeImages.EpisodeImageFilename;

                        if (!string.IsNullOrEmpty(remoteThumb) && !string.IsNullOrEmpty(localThumb))
                        {
                            if (GUIImageHandler.DownloadImage(remoteThumb, localThumb))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("EpisodeImages");
                            }
                        }
                        #endregion

                        #region Fanart
                        // stop download if we have exited window
                        if (StopDownload) break;
                        if (!TraktSettings.DownloadFanart) continue;

                        string remoteFanart = item.ShowImages.Fanart;
                        string localFanart = item.ShowImages.FanartImageFilename;

                        if (!string.IsNullOrEmpty(remoteFanart) && !string.IsNullOrEmpty(localFanart))
                        {
                            if (GUIImageHandler.DownloadImage(remoteFanart, localFanart))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("ShowImages");
                            }
                        }
                        #endregion
                    }
                })
                {
                    IsBackground = true,
                    Name = "ImageDownloader" + i.ToString()
                }.Start(groupList);
            }
        }

        #endregion
    }

    public class GUITraktWatchListEpisodeListItem : GUIListItem
    {
        public GUITraktWatchListEpisodeListItem(string strLabel) : base(strLabel) { }

        public object Item
        {
            get { return _Item; }
            set
            {
                _Item = value;
                INotifyPropertyChanged notifier = value as INotifyPropertyChanged;
                if (notifier != null) notifier.PropertyChanged += (s, e) =>
                {
                    if (s is TraktImage && e.PropertyName == "EpisodeImages")
                        SetImageToGui((s as TraktImage).EpisodeImages.EpisodeImageFilename);
                    if (s is TraktImage && e.PropertyName == "ShowImages")
                        this.UpdateItemIfSelected((int)TraktGUIWindows.WatchedListEpisodes, ItemId);
                };
            }
        } protected object _Item;

        /// <summary>
        /// Loads an Image from memory into a facade item
        /// </summary>
        /// <param name="imageFilePath">Filename of image</param>
        protected void SetImageToGui(string imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath)) return;

            ThumbnailImage = imageFilePath;
            IconImage = imageFilePath;
            IconImageBig = imageFilePath;

            // if selected and is current window force an update of thumbnail
            this.UpdateItemIfSelected((int)TraktGUIWindows.WatchedListEpisodes, ItemId);
        }
    }
}