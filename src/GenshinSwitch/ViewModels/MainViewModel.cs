﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenshinSwitch.Controls;
using GenshinSwitch.Controls.Notice;
using GenshinSwitch.Core;
using GenshinSwitch.Core.Settings;
using GenshinSwitch.Fetch.Launch;
using GenshinSwitch.Fetch.Regedit;
using GenshinSwitch.Helpers;
using GenshinSwitch.Models;
using GenshinSwitch.Models.Messages;
using GenshinSwitch.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.Threading;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;

namespace GenshinSwitch.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    [ObservableProperty]
    private Contact selectedItem = null!;

    public DispatcherTimer DispatcherTimer { get; } = new();
    public ObservableCollection<Contact> Contacts { get; set; } = new();

    public MainViewModel()
    {
        DispatcherTimer.Interval = TimeSpan.FromSeconds(2);
        DispatcherTimer.Tick += (s, e) =>
        {
            if (LaunchCtrl.TryGetProcessRegion(out string region))
            {
                string runningProd = region switch
                {
                    LaunchCtrl.RegionOVERSEA => GenshinRegedit.ProdOVERSEA,
                    LaunchCtrl.RegionCN or _ => GenshinRegedit.ProdCN,
                };

                foreach (Contact contact in Contacts)
                {
#if !DEBUG
                    if (!LaunchCtrl.GetElevated())
                    {
                        break;
                    }
#endif
                    if (contact.Prod == runningProd)
                    {
                        contact.ViewModel.IsRunning = true;
                    }
                    else
                    {
                        contact.ViewModel.IsRunning = false;
                    }
                }
            }
            else
            {
                foreach (Contact contact in Contacts)
                {
                    contact.ViewModel.IsRunning = false;
                }
            }
        };
        DispatcherTimer.Start();
    }

    [RelayCommand]
    private async Task AddContactAsync()
    {
        string prop = GenshinRegedit.ProdCN;
        var found = Settings.Contacts.Get().Where(kv => kv.Value.Prod == prop);

        if (found.Any())
        {
            Bubble.Warning($"当前账号已被添加为 {found.First().Value.AliasName}");
#if !DEBUG
            return;
#endif
        }

        AddContactContentDialog dialog = new()
        {
            XamlRoot = App.MainWindow.XamlRoot,
            RequestedTheme = App.MainWindow.ActualTheme,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Secondary)
        {
            await AddOrUpdateContactAsync(dialog.ContactMessage);
        }
    }

    [RelayCommand]
    private async Task RefreshContactAsync()
    {
        if (SelectedItem == null)
        {
            Bubble.Warning($"请选择要刷新的账号");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedItem.Cookie))
        {
            Bubble.Warning($"选中账号无 Cookie 信息");
            return;
        }
        await SelectedItem.ViewModel.FetchAllAsync();
    }

    [RelayCommand]
    private void CopyUid()
    {
        if (SelectedItem == null)
        {
            Bubble.Warning($"请选择要复制的账号");
            return;
        }

        string uid = SelectedItem?.Uid.ToString();

        if (!string.IsNullOrWhiteSpace(uid))
        {
            ClipboardHelper.SetText(uid);
            Bubble.Information($"角色UID:{uid}已复制到剪贴板");
        }
        else
        {
            Bubble.Warning($"选中账号无 Cookie 信息");
            return;
        }
    }

    [RelayCommand]
    private void CopyCookie()
    {
        if (SelectedItem == null)
        {
            Bubble.Warning($"请选择要复制的账号");
            return;
        }

        string cookie = SelectedItem?.Cookie;

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            ClipboardHelper.SetText(cookie);
            Bubble.Information($"Cookie 已复制到剪贴板");
        }
        else
        {
            Bubble.Warning($"选中账号无 Cookie 信息");
            return;
        }
    }

    [RelayCommand]
    private async Task EditContactAsync()
    {
        if (SelectedItem == null)
        {
            Bubble.Warning($"请选择要编辑的账号");
            return;
        }

        AddContactContentDialog dialog = new(SelectedItem)
        {
            XamlRoot = App.MainWindow.XamlRoot,
            RequestedTheme = App.MainWindow.ActualTheme,
            Title = "编辑账号",
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Secondary)
        {
            await AddOrUpdateContactAsync(dialog.ContactMessage);
        }
    }

    [RelayCommand]
    private async Task DeleteContactAsync()
    {
        if (SelectedItem == null)
        {
            Bubble.Warning($"请选择要删除的账号");
            return;
        }
        if (await new MessageBoxX($"是否确定要删除账号「{SelectedItem.AliasName}」？", "删除账号").ShowAsync() == ContentDialogResult.Secondary)
        {
            await AddOrUpdateContactAsync(new ContactMessage()
            {
                Type = ContactMessage.ContactMessageType.Removed,
                Contact = SelectedItem,
            });
        }
    }

    public void OnContactListViewItemClick(object sender, ItemClickEventArgs e)
    {
        if (ListViewHelper.TryRaiseItemDoubleClick(sender, e))
        {
            LaunchGameAsync((Contact)e.ClickedItem).Forget();
        }
    }

    public void OnConcactDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        _ = sender;
        _ = e;

        Dictionary<string, Contact> dict = new();
        Dictionary<string, Contact> contacts = Settings.Contacts.Get();

        foreach (Contact contact in Contacts)
        {
            dict.Add(contact.Guid, contacts[contact.Guid]);
        }
        Settings.Contacts.Set(dict);
        SettingsManager.Save();
    }

    public async Task LaunchGameAsync(Contact contact)
    {
        try
        {
            await LaunchCtrl.LaunchAsync(relaunchMethod: Settings.RelaunchMethod.Get(), launchParameter: new LaunchParameter()
            {
                Region = contact.RegionName,
                Prod = contact.Prod,
            });
        }
        catch (Exception e)
        {
            NoticeService.AddNotice(string.Empty, e.Message);
        }
    }

    private async Task AddOrUpdateContactAsync(ContactMessage msg)
    {
        Dictionary<string, Contact> dict = Settings.Contacts.Get();

        if (msg.Type == ContactMessage.ContactMessageType.Added)
        {
            dict.Add(msg.Contact.Guid, msg.Contact);
            Contacts.Add(msg.Contact);
            Bubble.Success($"添加 {msg.Contact.AliasName} 成功");
            if (!string.IsNullOrWhiteSpace(msg.Contact.Cookie))
            {
                await msg.Contact.ViewModel.FetchAllAsync();
            }
        }
        else if (msg.Type == ContactMessage.ContactMessageType.Edited)
        {
            if (dict.ContainsKey(msg.Contact.Guid))
            {
                dict.Remove(msg.Contact.Guid);
                dict.Add(msg.Contact.Guid, msg.Contact);
                if (!string.IsNullOrWhiteSpace(msg.Contact.Cookie))
                {
                    await msg.Contact.ViewModel.FetchAllAsync();
                }
            }
            else
            {
                Logger.Fatal($"[AddOrUpdateContact] Lag of {msg.Contact.Guid}");
                Debugger.Break();
                return;
            }
        }
        else if (msg.Type == ContactMessage.ContactMessageType.Removed)
        {
            dict.Remove(msg.Contact.Guid);

            if (Contacts.Where(c => c.Guid == msg.Contact.Guid).FirstOrDefault() is Contact contactToRemove)
            {
                Contacts.Remove(contactToRemove);
            }
        }

        Settings.Contacts.Set(dict);
        SettingsManager.Save();
        WeakReferenceMessenger.Default.Send(msg);
    }
}
