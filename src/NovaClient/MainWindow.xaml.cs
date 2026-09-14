using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using NovaClient.Models;
using NovaClient.Services;
namespace NovaClient;
public partial class MainWindow : Window
{
    private readonly ProfileService _profiles = new();
    private readonly ModrinthService _modrinth = new();
    private readonly MicrosoftAuthService _auth = new();
    private readonly ObservableCollection<Profile> _profileItems = [];
    private Profile? _current;
    public MainWindow(){ InitializeComponent(); Loaded += async (_,_) => await InitializeAsync(); }
    private async Task InitializeAsync(){ foreach(var p in await _profiles.LoadAsync()) _profileItems.Add(p); ProfilesList.ItemsSource=_profileItems; _current=_profileItems.FirstOrDefault(); ProfilesList.SelectedItem=_current; DataPathText.Text=_profiles.InstancesRoot; UpdateCurrentProfileUi(); }
    private void ShowPage(string name){ HomePage.Visibility=LibraryPage.Visibility=DiscoverPage.Visibility=DownloadsPage.Visibility=AccountPage.Visibility=SettingsPage.Visibility=Visibility.Collapsed; var target=name switch{"Library"=>LibraryPage,"Discover"=>DiscoverPage,"Downloads"=>DownloadsPage,"Account"=>AccountPage,"Settings"=>SettingsPage,_=>HomePage}; target.Visibility=Visibility.Visible; PageTitle.Text=name; if(Resources["PageIn"] is Storyboard sb) sb.Begin(); }
    private void Nav_Click(object sender,RoutedEventArgs e){ if(sender is Button b && b.Tag is string page) ShowPage(page); }
    private void UpdateCurrentProfileUi(){ if(_current is null){CurrentProfileName.Text="No profile";CurrentProfileMeta.Text="Create a profile to begin.";return;} CurrentProfileName.Text=_current.Name; CurrentProfileMeta.Text=$"Minecraft {_current.MinecraftVersion} • {_current.Loader} • {_current.MemoryMb/1024d:0.#} GB RAM"; ProfileCountText.Text=$"{_profileItems.Count} profile{(_profileItems.Count==1?"":"s")}"; }
    private async void NewProfile_Click(object sender,RoutedEventArgs e){ var p=new Profile{Name=$"Profile {_profileItems.Count+1}",MinecraftVersion="1.21.1",Loader="Fabric",MemoryMb=6144}; _profileItems.Add(p); _current=p; ProfilesList.SelectedItem=p; _profiles.GetInstancePath(p); await _profiles.SaveAsync(_profileItems); UpdateCurrentProfileUi(); StatusText.Text="Profile created"; }
    private void ProfilesList_SelectionChanged(object sender,SelectionChangedEventArgs e){ if(ProfilesList.SelectedItem is Profile p){_current=p;UpdateCurrentProfileUi();} }
    private async void Search_Click(object sender,RoutedEventArgs e)=>await SearchModsAsync();
    private async void SearchBox_KeyDown(object sender,KeyEventArgs e){ if(e.Key==Key.Enter) await SearchModsAsync(); }
    private async Task SearchModsAsync(){ try{StatusText.Text="Searching Modrinth…"; var v=(DiscoverVersion.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _current?.MinecraftVersion ?? "1.21.1"; var loader=_current?.Loader ?? "Fabric"; ModsList.ItemsSource=await _modrinth.SearchAsync(SearchBox.Text,v,loader); StatusText.Text=$"{ModsList.Items.Count} mods found";}catch(Exception ex){StatusText.Text="Search failed";MessageBox.Show(ex.Message,"Nova Client",MessageBoxButton.OK,MessageBoxImage.Warning);} }
    private async void InstallMod_Click(object sender,RoutedEventArgs e){ if(sender is not Button {Tag:ModrinthProject project}) return; if(_current is null){MessageBox.Show("Create or select a profile first.");return;} try{ShowPage("Downloads");DownloadTitle.Text=$"Installing {project.Title}";DownloadDetail.Text=$"Target: {_current.Name}";DownloadProgress.Value=0; var progress=new Progress<double>(x=>DownloadProgress.Value=x*100); var file=await _modrinth.InstallAsync(project,_current,_profiles,progress);DownloadProgress.Value=100;DownloadTitle.Text=$"Installed {project.Title}";DownloadDetail.Text=file;StatusText.Text="Install complete";}catch(Exception ex){DownloadTitle.Text="Install failed";DownloadDetail.Text=ex.Message;StatusText.Text="Install failed";} }
    private async void MicrosoftLogin_Click(object sender,RoutedEventArgs e){ try{AccountState.Text="Starting Microsoft sign-in…"; var session=await _auth.BeginDeviceCodeAsync(); DeviceCodeText.Text=$"Code: {session.UserCode}\n{session.Message}"; _auth.OpenVerificationPage(session); AccountState.Text="Complete sign-in in your browser";}catch(Exception ex){AccountState.Text="Microsoft login needs app registration";DeviceCodeText.Text=ex.Message+"\n\nNova intentionally does not ship someone else's secret/client identity.";} }
    private void MemorySlider_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e){ if(MemoryLabel is null)return; MemoryLabel.Text=$"{(int)e.NewValue} GB"; if(_current is not null)_current.MemoryMb=(int)e.NewValue*1024; }
    private void Play_Click(object sender,RoutedEventArgs e){ if(_current is null){MessageBox.Show("Create a profile first.");return;} var path=_profiles.GetInstancePath(_current); StatusText.Text="Launcher core next milestone"; MessageBox.Show($"Profile is ready at:\n{path}\n\nThe first preview already manages profiles and mods. The actual Minecraft process/bootstrap is the next launcher-core milestone.","Nova Client Preview",MessageBoxButton.OK,MessageBoxImage.Information); }
}
