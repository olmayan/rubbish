﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Gtk;

namespace ImageViewer
{
	public class MainWindow : Window
	{
		public MainWindow () : base("Image Viewer")
		{
			Destroyed += delegate { Application.Quit(); };

			addr = new Entry();
			addr.KeyReleaseEvent += delegate(object o, KeyReleaseEventArgs args) {
				Console.WriteLine(args.Event.Key.ToString());
				if (args.Event.Key == Gdk.Key.Return)
				{
					LoadFolder(addr.Text, addr);
				}
			};

			list = new ListStore(typeof(string), typeof(Gdk.Pixbuf), typeof(ItemData));
			list.SetSortColumnId(2, SortType.Ascending);
			list.SetSortFunc(2, (ITreeModel model, TreeIter a0, TreeIter b0) => {
				ItemData a = (ItemData)model.GetValue(a0, 2);
				ItemData b = (ItemData)model.GetValue(b0, 2);
				if (a == null)
					return (b == null) ? 0 : -1;
				if (b == null)
					return 1;

				if (a.IsFile && !b.IsFile)
					return 1;
				if (!a.IsFile && b.IsFile)
					return -1;

				int result = 0;

				switch (SortKey.Instance.Key)
				{
				case SortKeys.Name:
					result = string.Compare(a.Name, b.Name, false);
					break;
				case SortKeys.Extension:
					int extcmp = string.Compare(a.Extension, b.Extension, false);
					result = (extcmp == 0) ? string.Compare(a.Name, b.Name, false) : extcmp;
					break;
				case SortKeys.Size:
					result = a.Size.CompareTo(b.Size);
					break;
				case SortKeys.Date:
					result = a.Date.CompareTo(b.Date);
					break;
				}
				return result;
			} );

			view = new IconView(list);
			view.TextColumn = 0;
			view.PixbufColumn = 1;
			view.ItemWidth = 96;
			view.ItemActivated += delegate(object o, ItemActivatedArgs args) {
				TreeIter iter;
				list.GetIter(out iter, args.Path);
				ItemData itemData = (ItemData)list.GetValue(iter, 2);
				if (itemData.IsFile)
					return;
				LoadFolder(itemData.FullName);
			};
			view.KeyReleaseEvent += delegate(object o, KeyReleaseEventArgs args) {
				if (args.Event.Key == Gdk.Key.BackSpace)
				{
					if (curdir.Root.FullName == curdir.FullName)
						return;
					LoadFolder(curdir.Parent.FullName);
					view.Activate();
				}
			};

			ScrolledWindow scrolled = new ScrolledWindow();
			scrolled.HscrollbarPolicy = PolicyType.Automatic;
			scrolled.VscrollbarPolicy = PolicyType.Automatic;
			scrolled.Add(view);

			statusbar = new Statusbar();

			Box box = new VBox();
			box.Homogeneous = false;
			box.PackStart(addr, false, false, 0);
			box.PackStart(scrolled, true, true, 0);
			box.PackStart(statusbar, false, false, 0);

			Add(box);

			SetSizeRequest(400, 300);

			icon = RenderIconPixbuf(Stock.Directory, IconSize.Dialog);
			fileIcon = RenderIconPixbuf(Stock.File, IconSize.Dialog);
			mimeicons = new Dictionary<string, Gdk.Pixbuf>();
			noicons = new List<string>();

			box.FocusChain = new Widget[] { scrolled, addr };

			string path = (
				System.Environment.OSVersion.Platform == PlatformID.MacOSX ||
				System.Environment.OSVersion.Platform == PlatformID.Unix)
				? Environment.GetEnvironmentVariable("HOME")
				: Environment.GetEnvironmentVariable("HOMEDRIVE") + Environment.GetEnvironmentVariable("HOMEPATH");
			LoadFolder(path);

			Console.WriteLine(GLib.ContentType.GetIcon("text/plain").ToString());
		}

		public void LoadFolder(string pathname, Widget focus = null)
		{
			addr.Sensitive = view.Sensitive = false;
			int selStart, selEnd;
			addr.GetSelectionBounds(out selStart, out selEnd);

			Thread thread = new Thread(new ThreadStart(delegate {
				DirectoryInfo dir = (pathname == string.Empty) ? null : new DirectoryInfo(pathname);
				if (dir == null || !dir.Exists)
				{
					Application.Invoke(delegate {
						MessageDialog dlg = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok, "Directory does not exist!");
						dlg.Run();
						dlg.Destroy();
					} );
				}
				else
				{
					List<DirectoryInfo> dirInfos = Directory.GetDirectories(pathname).ToList()
						.Select(s => new DirectoryInfo(s)).Where(di => (di.Attributes & FileAttributes.Hidden) == 0).ToList();
					List<object[]> items = dirInfos.Select(di => new object[] { 
						di.Name,
						icon,
						new ItemData(di) } ).ToList();

					List<FileInfo> fileInfos = Directory.GetFiles(pathname).ToList()
						.Select(s => new FileInfo(s)).Where(fi => (fi.Attributes & FileAttributes.Hidden) == 0).ToList();
					items.AddRange(fileInfos.Select(fi => new object[] { 
						fi.Name,
						GetMimeIcon(fi.FullName),
						new ItemData(fi) } ).ToList());
						
					Application.Invoke(delegate { list.Clear();	} );
					foreach (object[] values in items)
						Application.Invoke(delegate { list.AppendValues(values); } );

					curdir = dir;
					Application.Invoke(delegate { addr.Text = pathname; } );
				}

				Application.Invoke(delegate {
					addr.Sensitive = view.Sensitive = true;
					((focus == null) ? view : focus).GrabFocus();
					if (focus == addr) addr.SelectRegion(selStart, selEnd);
				} );
			} ));

			thread.Start();
		}

		void LoadMimeIcons()
		{
		}

		Gdk.Pixbuf GetMimeIcon(string filename)
		{
			byte data;
			bool uncertain;

			string ctype = GLib.ContentType.Guess(filename, out data, 0, out uncertain);

			if (uncertain || noicons.Exists(s => s == ctype))
				return fileIcon;

			if (mimeicons.ContainsKey(ctype))
				return mimeicons[ctype];

			IconInfo info = IconTheme.Default.LookupIcon(GLib.ContentType.GetIcon(ctype), 48, IconLookupFlags.UseBuiltin);
			Gdk.Pixbuf pixbuf;
			try
			{
				pixbuf = info.LoadIcon();
			}
			catch (Exception)
			{
				pixbuf = null;
			}

			if (pixbuf == null)
				return fileIcon;

			mimeicons.Add(ctype, pixbuf);
			return pixbuf;
		}

		Entry addr;
		DirectoryInfo curdir;
		ListStore list;
		IconView view;
		Gdk.Pixbuf icon, fileIcon;
		Statusbar statusbar;
		Dictionary<string, Gdk.Pixbuf> mimeicons;
		List<string> noicons;
	}
}
