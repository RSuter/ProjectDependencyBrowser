﻿//-----------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="MyToolkit">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>http://projectdependencybrowser.codeplex.com/license</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

extern alias build;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using GlobalHotKey;
using build::MyToolkit.Build;
using MyToolkit.Controls;
using MyToolkit.Messaging;
using MyToolkit.Model;
using MyToolkit.Mvvm;
using MyToolkit.Serialization;
using MyToolkit.Storage;
using MyToolkit.Utilities;
using ProjectDependencyBrowser.Messages;
using ProjectDependencyBrowser.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;

namespace ProjectDependencyBrowser.Views
{
    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class. </summary>
        public MainWindow()
        {
            InitializeComponent();

            ViewModelHelper.RegisterViewModel(Model, this);

            //#if !DEBUG
            //            ProjectDetailsButton.Visibility = Visibility.Collapsed;
            //#endif

            Closed += delegate { Model.CallOnUnloaded(); };
            Activated += delegate { FocusProjectNameFilter(); };
            Loaded += delegate { RegisterHotKey(); };
            SizeChanged += OnSizeChanged;

            Model.PropertyChanged += async (sender, args) =>
            {
                if (args.IsProperty<MainWindowModel>(i => i.IsLoaded))
                {
                    if (Model.IsLoaded)
                    {
                        Tabs.SelectedIndex = 1;
                        await Task.Delay(250);
                        FocusProjectNameFilter();
                    }
                }
                else if (args.IsProperty<MainWindowModel>(i => i.SelectedProject))
                {
                    ProjectReferencesList.Filter = string.Empty;
                    NuGetReferencesList.Filter = string.Empty;
                    AssemblyReferencesList.Filter = string.Empty;
                }
            };

            KeyUp += (sender, args) =>
            {
                var selectedTextBox = FocusManager.GetFocusedElement(this) as System.Windows.Controls.TextBox;
                var backKeyPressed = args.Key == Key.Back && (selectedTextBox == null || selectedTextBox.Style == App.Current.FindResource("SelectableTextBlock"));
                if (backKeyPressed || (args.Key == Key.BrowserBack) || (args.Key == Key.System && args.SystemKey == Key.Left))
                {
                    Model.ShowPreviousProjectCommand.TryExecute();
                }
            };

            Messenger.Default.Register<ShowProjectMessage>(ShowProject);

            CheckForApplicationUpdate();
            LoadWindowState();
        }

        /// <summary>Gets the view model. </summary>
        public MainWindowModel Model
        {
            get { return (MainWindowModel)Resources["ViewModel"]; }
        }

        private void RegisterHotKey()
        {
            if (Model.EnableShowApplicationHotKey)
            {
                try
                {
                    var hotKeyManager = new HotKeyManager();
                    hotKeyManager.Register(new HotKey(Key.V, ModifierKeys.Windows | ModifierKeys.Control));
                    hotKeyManager.KeyPressed += (sender, args) =>
                    {
                        System.Windows.Application.Current.MainWindow.Activate();

                        if (System.Windows.Application.Current.MainWindow.WindowState == WindowState.Minimized)
                            System.Windows.Application.Current.MainWindow.WindowState = WindowState.Normal;
                    };
                }
                catch (Exception exception)
                {
                    MessageBox.Show("Could not register hot key: \n" + exception.Message, "Error");
                }
            }
        }

        private void FocusProjectNameFilter()
        {
            Keyboard.Focus(ProjectNameFilter);

            ProjectNameFilter.Focus();
            ProjectNameFilter.SelectAll();
        }

        private void OnProjectNameFilterGotFocus(object sender, RoutedEventArgs e)
        {
            ProjectNameFilter.SelectAll();
        }

        private async void CheckForApplicationUpdate()
        {
            var updater = new ApplicationUpdater(
                "ProjectDependencyBrowser.msi",
                GetType().Assembly,
                "http://rsuter.com/Projects/ProjectDependencyBrowser/updates.xml");

            await updater.CheckForUpdate(this);
        }

        private void OnSelectDirectory(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderBrowserDialog();
            dlg.SelectedPath = Model.RootDirectory;
            dlg.Description = "Select root directory: ";
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                Model.RootDirectory = dlg.SelectedPath;
        }

        private void OnOpenHyperlink(object sender, RoutedEventArgs e)
        {
            var uri = ((Hyperlink)sender).NavigateUri;
            Process.Start(uri.ToString());
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Handled)
            {
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    e.Handled = true;

                    if (Model.SelectedProjectSolutions.Any())
                        Model.TryOpenSolution(Model.SelectedProjectSolutions.First());
                }
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Handled)
            {
                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    ProjectList.Focus();

                    if (Model.FilteredProjects.Any())
                    {
                        ProjectList.SelectedIndex = 0;
                        ProjectList.ScrollIntoView(ProjectList.SelectedItem);
                        ((ListBoxItem)ProjectList.ItemContainerGenerator.ContainerFromItem(ProjectList.SelectedItem)).Focus();
                    }
                }
            }
        }

        private void OnSolutionDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            var solution = (VsSolution)((ListBox)sender).SelectedItem;
            if (solution != null)
                Model.TryOpenSolution(solution);
        }

        private void OnSolutionKeyUp(object sender, KeyEventArgs e)
        {
            if (!e.Handled)
            {
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    var solution = (VsSolution)((ListBox)sender).SelectedItem;
                    if (solution != null)
                        Model.TryOpenSolution(solution);
                }
            }
        }

        private void OnProjectDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            var projectReference = (VsProjectReference)((FilterListBox)sender).SelectedItem;
            if (projectReference != null)
            {
                Model.SelectProjectReference(projectReference);
                // TODO: Jump to selected item
            }
        }

        private void OnProjectKeyUp(object sender, KeyEventArgs e)
        {
            if (!e.Handled)
            {
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    var project = (VsProject)((FilterListBox)sender).SelectedItem;
                    if (project != null)
                        Model.SelectProject(project);
                }
            }
        }

        private void LoadWindowState()
        {
            Width = ApplicationSettings.GetSetting("WindowWidth", Width);
            Height = ApplicationSettings.GetSetting("WindowHeight", Height);
            Left = ApplicationSettings.GetSetting("WindowLeft", Left);
            Top = ApplicationSettings.GetSetting("WindowTop", Top);
            WindowState = ApplicationSettings.GetSetting("WindowState", WindowState);

            if (Left == double.NaN)
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        protected override void OnClosed(EventArgs e)
        {
            ApplicationSettings.SetSetting("WindowWidth", Width);
            ApplicationSettings.SetSetting("WindowHeight", Height);
            ApplicationSettings.SetSetting("WindowLeft", Left);
            ApplicationSettings.SetSetting("WindowTop", Top);
            ApplicationSettings.SetSetting("WindowState", WindowState);

            Model.CallOnUnloaded();
        }

        private void ShowProject(ShowProjectMessage message)
        {
            Model.ClearFilter();
            Model.SelectedProject = message.Project;

            if (Model.SelectedProject != null)
            {
                ProjectList.ScrollIntoView(Model.SelectedProject);
                ProjectTabs.SelectedIndex = 0;
            }
        }

        private void OnSelectItemFromFilteredListBox(object sender, MouseButtonEventArgs args)
        {
            var item = ((FilterListBox)sender).SelectedItem;
            if (item != null)
                Model.SelectObjectCommand.Execute(item);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            BorderThickness = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnMinimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximize(object sender, RoutedEventArgs e)
        {
            if (WindowState != WindowState.Maximized)
                WindowState = WindowState.Maximized;
            else
                WindowState = WindowState.Normal;
        }
    }
}
