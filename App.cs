using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DailyEvents
{
  public class App : Form
  {
    static private readonly string LoggedUser = Environment.UserName;

    static private readonly bool SkipTrayIcon = false;

    static private readonly short MaxGroups          = 5;
    static private readonly short GroupNameMaxLength = 30;
    static private readonly short GroupCodeMaxLength = 15;
    static private readonly short CommentMaxLength   = 70;

    private readonly ApiClient api = new ApiClient();

    private readonly ContextMenu trayMenu = new ContextMenu();
    private NotifyIcon trayIcon;

    private bool newVersionBalloonShown = false;

    [STAThread]
    static public void Main()
    {
      Application.Run(new App());
    }

    public App()
    {
      RebuildTrayMenu();

      if (SkipTrayIcon)
        InitToolStrip();
      else
        InitTrayIcon();
    }

    private void InitToolStrip()
    {
      Controls.Add(new ToolStrip() {
        ContextMenu = trayMenu
      });
      MouseDown += OnRefreshGroup;
    }

    private void InitTrayIcon()
    {
      trayIcon = new NotifyIcon();
      trayIcon.Text = "Daily Events";
      trayIcon.ContextMenu = trayMenu;
      trayIcon.MouseDown += OnRefreshGroup;
      trayIcon.Visible = true;
      trayIcon.BalloonTipClicked += OnBalloonTipClicked;

      SetAppIcon();

      if (AppInfo.CurrentVersionNumber < AppInfo.LatestVersionNumber)
      {
        newVersionBalloonShown = true;
        ShowInfo("New version available, grab it while it's hot!");
      }
    }

    private void RebuildTrayMenu()
    {
      RebuildTrayMenu(null, null);
    }

    private void RebuildTrayMenu(List<Status> statuses, List<Comment> comments)
    {
      Invalidate();
      trayMenu.MenuItems.Clear();

      if (IsCurrentGroupSet())
      {
        trayMenu.MenuItems.Add(GetCurrentGroupName());
        trayMenu.MenuItems.Add("-");

        if (statuses != null && statuses.Count > 0)
        {
          foreach (var status in statuses)
          {
            string participant = status.participant;
            trayMenu.MenuItems.Add(participant);
          }
        }
        else
        {
          trayMenu.MenuItems.Add("(nobody's attending yet)");
        }
        trayMenu.MenuItems.Add("-");
        trayMenu.MenuItems.Add("I'm in", OnReplyYes);
        trayMenu.MenuItems.Add("I'm out", OnReplyNo);
        trayMenu.MenuItems.Add("-");
        trayMenu.MenuItems.Add("Add comment", OnNewComment);
        trayMenu.MenuItems.Add("-");

        if (comments != null && comments.Count > 0)
        {
          foreach (var comment in comments)
          {
            string participant = comment.participant;
            string commentText = comment.comment;
            string timestamp   = comment.timestamp;
            string localTime   = DateUtils.FormatTime(timestamp);

            trayMenu.MenuItems.Add(localTime + " " + participant + ": " + commentText);
          }
          trayMenu.MenuItems.Add("-");
        }
      }
      else
      {
        trayMenu.MenuItems.Add("(no group is set)");
        trayMenu.MenuItems.Add("-");
      }
      trayMenu.MenuItems.Add(BuildGroupsMenu());

      trayMenu.MenuItems.Add("-");
      trayMenu.MenuItems.Add("About", OnAbout);
      trayMenu.MenuItems.Add("Exit", OnExit);
    }

    MenuItem BuildGroupsMenu()
    {
      MenuItem menu = new MenuItem("Groups");

      dynamic joinedGroups = Settings.Groups;
      short numberOfGroupsAdded = 0;

      foreach (var group in joinedGroups)
      {
        if (numberOfGroupsAdded == MaxGroups)
          break;

        // menu.MenuItems.Add(group.Value, (EventHandler)OnSwitchGroup); // (EventHandler)((sender, e) => {})

        MenuItem subMenu = new MenuItem(group.Value);

        subMenu.MenuItems.Add("Switch to", OnSwitchToGroup);
        subMenu.MenuItems.Add("Invite people", OnInvitePeople);
        subMenu.MenuItems.Add("Rename group", OnRenameGroup);
        subMenu.MenuItems.Add("Leave group", OnLeaveGroup);
        
        menu.MenuItems.Add(subMenu);

        numberOfGroupsAdded++;
      }
      if (joinedGroups.Count > 0)
      {
        menu.MenuItems.Add("-");
      }
      menu.MenuItems.Add("Join group", OnJoinGroup);
      menu.MenuItems.Add("Create group", OnCreateGroup);

      return menu;
    }

    protected override void OnLoad(EventArgs e)
    {
      Visible = SkipTrayIcon;
      ShowInTaskbar = false;
      base.OnLoad(e);
    }

    protected override void Dispose(bool isDisposing)
    {
      if (isDisposing)
      {
        if (trayIcon != null)
          trayIcon.Dispose();
      }
      base.Dispose(isDisposing);
    }

    private void OnRefreshGroup(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
      {
        try
        {
          if (IsCurrentGroupSet())
          {
            SetLoadingIcon();
            Result details = api.GetEvent(Settings.CurrentGroup);
            SetAppIcon();

            RebuildTrayMenu(details.statuses, details.comments);
          }
          else
          {
            RebuildTrayMenu();
          }
        }
        catch (Exception ex)
        {
          ShowNetworkError(ex);
        }
        finally
        {
          SetAppIcon();
        }
      }
    }

    private void OnReplyYes(object sender, EventArgs e)
    {
      try
      {
        SetLoadingIcon();
        api.SetStatus(Settings.CurrentGroup, LoggedUser, "yes");
        SetAppIcon();

        ShowInfo("Attendance confirmed!");
      }
      catch (Exception ex)
      {
        ShowNetworkError(ex);
      }
      finally
      {
        SetAppIcon();
      }
    }
    
    private void OnReplyNo(object sender, EventArgs e)
    {
      try
      {
        SetLoadingIcon();
        api.SetStatus(Settings.CurrentGroup, LoggedUser, "no");
        SetAppIcon();

        ShowInfo("Attendance cancelled.");
      }
      catch (Exception ex)
      {
        ShowNetworkError(ex);
      }
      finally
      {
        SetAppIcon();
      }
    }

    private void OnNewComment(object sender, EventArgs e)
    {
      string comment = Prompt.Show("Add Comment", "Enter your comment:", CommentMaxLength);

      if (comment.Length > 0)
      {
        try
        {
          SetLoadingIcon();
          api.AddComment(Settings.CurrentGroup, LoggedUser, comment);
          SetAppIcon();

          ShowInfo("Comment added.");
        }
        catch (Exception ex)
        {
          ShowNetworkError(ex);
        }
        finally
        {
          SetAppIcon();
        }
      }
    }

    private void OnCreateGroup(object sender, EventArgs e)
    {
      if (!CanJoinOrCreateGroups())
        return;
      
      string name = Prompt.Show("Create Group", "Enter the group's name:", GroupNameMaxLength);
      
      if (name.Length == 0)
        return;
      
      if (Settings.Groups.ContainsValue(name))
      {
        MessageBox.Show("A group named '" + name + "' already exists, please enter another name.", "Existing Group");
        OnCreateGroup(sender, e);
      }
      else
      {
        try
        {
          SetLoadingIcon();
          string id = api.CreateGroup().id;
          SetAppIcon();

          dynamic groups = Settings.Groups;
          groups[id] = name;
          
          Settings.Groups = groups;
          Settings.CurrentGroup = id;
          
          ShowInfo("Group created!");
        }
        catch (Exception ex)
        {
          ShowNetworkError(ex);
        }
        finally
        {
          SetAppIcon();
        }
      }
    }
    
    private void OnJoinGroup(object sender, EventArgs e)
    {
      if (!CanJoinOrCreateGroups())
        return;
      
      string code = Prompt.Show("Join Group", "Group code:", GroupCodeMaxLength);
      
      if (code.Length == 0)
        return;
      
      string name = Prompt.Show("Join Group", "Group name:", GroupNameMaxLength);

      if (name.Length == 0)
        return;

      if (Settings.Groups.ContainsValue(name))
      {
        MessageBox.Show("A group named '" + name + "' already exists, please enter another name.", "Existing Group");
        OnJoinGroup(sender, e);
      }
      else
      {
        try
        {
          SetLoadingIcon();

          dynamic groups = Settings.Groups;
          groups [code] = name;
          
          Settings.Groups = groups;
          Settings.CurrentGroup = code;
          
          ShowInfo("Joined group!");
        }
        catch (Exception ex)
        {
          ShowNetworkError(ex);
        }
        finally
        {
          SetAppIcon();
        }
      }
    }

    private void OnSwitchToGroup(object sender, EventArgs e)
    {
      string name = GetParentMenuText(sender);
      Settings.CurrentGroup = GetGroupId(name);

      ShowInfo("Switched group.");
    }

    private void OnInvitePeople(object sender, EventArgs e)
    {
      string groupName = GetParentMenuText(sender);
      string groupId   = GetGroupId(groupName);

      try
      {
        SetLoadingIcon();
        string code = api.GetGroup(groupId).code;
        SetAppIcon();

        Clipboard.SetText(code);
        
        MessageBox.Show("Send this code to guests: " + code + ".\nThe code was just copied to your clipboard.", "Invite People");
      }
      catch (Exception ex)
      {
        ShowNetworkError(ex);
      }
      finally
      {
        SetAppIcon();
      }
    }

    private void OnRenameGroup(object sender, EventArgs e)
    {
      string currentName = GetParentMenuText(sender);
      string groupId     = GetGroupId(currentName);
      
      string newName = Prompt.Show("Rename Group", "Enter the group's new name:", GroupNameMaxLength);
      
      dynamic groups  = Settings.Groups;
      groups[groupId] = newName;
      
      Settings.Groups = groups;

      ShowInfo("Group renamed.");
    }

    private void OnLeaveGroup(object sender, EventArgs e)
    {
      string groupName = GetParentMenuText(sender);
      string groupId   = GetGroupId(groupName);

      dynamic groups = Settings.Groups;
      groups.Remove(groupId);
      
      Settings.Groups = groups;

      if (Settings.CurrentGroup == groupId)
        Settings.CurrentGroup = "";

      ShowInfo("Group removed.");
    }

    private string GetParentMenuText(object sender)
    {
      return ((MenuItem)((MenuItem)sender).Parent).Text;
    }

    private bool CanJoinOrCreateGroups()
    {
      bool maxGroupsReached = Settings.Groups.Count >= MaxGroups;
      
      if (maxGroupsReached)
      {
        MessageBox.Show("Sorry, this client is limited to " + MaxGroups + " groups for now.", "Max Groups Reached");
      }
      return !maxGroupsReached;
    }

    private bool IsCurrentGroupSet()
    {
      string currentGroup = Settings.CurrentGroup;
      return currentGroup.Length > 0 && Settings.Groups.ContainsKey(currentGroup);
    }

    private string GetCurrentGroupName()
    {
      string groupId = Settings.CurrentGroup;
      return Settings.Groups[groupId];
    }

    private string GetGroupId(string name)
    {
      foreach (var group in Settings.Groups)
      {
        if (group.Value == name)
        {
          return group.Key;
        }
      }
      throw new ApplicationException("Group not found: " + name);
    }

    private void ShowInfo(string message)
    {
      if (trayIcon != null)
        trayIcon.ShowBalloonTip(10000, "", message, ToolTipIcon.None);
    }
    
    private void ShowNetworkError(Exception ex)
    {
      Console.Write(ex);
      
      if (trayIcon != null)
        trayIcon.ShowBalloonTip(10000, "Network Error", "Please wait a bit and try again.", ToolTipIcon.Error);
    }
    
    private void SetAppIcon()
    {
      if (trayIcon != null)
        trayIcon.Icon = new Icon(SystemIcons.Application, 40, 40);
    }
    
    private void SetLoadingIcon()
    {
      if (trayIcon != null)
        trayIcon.Icon = new Icon(SystemIcons.Question, 40, 40);
    }

    private void OnBalloonTipClicked(object sender, EventArgs e)
    {
      bool mouseButtonLeft = ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left);

      if (newVersionBalloonShown && mouseButtonLeft)
      {
        System.Diagnostics.Process.Start("https://dl.dropboxusercontent.com/u/1210246/DailyEvents/download/DailyEvents-windows.zip");
        newVersionBalloonShown = false;
      }
    }

    private void OnAbout(object sender, EventArgs e)
    {
      About.Show();
    }

    private void OnExit(object sender, EventArgs e)
    {
      Application.Exit();
    }
  }
}
