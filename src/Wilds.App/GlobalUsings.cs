// Copyright (c) Files Community
// Licensed under the MIT License.

// System
global using global::System;
global using global::System.Collections;
global using global::System.Collections.Generic;
global using global::System.Collections.ObjectModel;
global using global::System.Linq;
global using global::System.Threading;
global using global::System.Threading.Tasks;
global using global::System.ComponentModel;
global using global::System.Diagnostics;
global using global::System.Text.Json;
global using global::System.Text.Json.Serialization;
global using SystemIO = global::System.IO;

// CommunityToolkit.Mvvm
global using global::CommunityToolkit.Mvvm.ComponentModel;
global using global::CommunityToolkit.Mvvm.DependencyInjection;
global using global::CommunityToolkit.Mvvm.Input;
global using global::CommunityToolkit.Mvvm.Messaging;

// Wilds.App
global using global::Wilds.App.Helpers;
global using global::Wilds.App.Extensions;
global using global::Wilds.App.Utils;
global using global::Wilds.App.Utils.Cloud;
global using global::Wilds.App.Utils.FileTags;
global using global::Wilds.App.Utils.Git;
global using global::Wilds.App.Utils.Library;
global using global::Wilds.App.Utils.Serialization;
global using global::Wilds.App.Utils.Shell;
global using global::Wilds.App.Utils.StatusCenter;
global using global::Wilds.App.Utils.Storage;
global using global::Wilds.App.Utils.Taskbar;
global using global::Wilds.App.Data.Behaviors;
global using global::Wilds.App.Data.Commands;
global using global::Wilds.App.Data.Contexts;
global using global::Wilds.App.Data.Contracts;
global using global::Wilds.App.Data.EventArguments;
global using global::Wilds.App.Data.Factories;
global using global::Wilds.App.Data.Items;
global using global::Wilds.App.Data.Models;
global using global::Wilds.App.Data.Parameters;
global using global::Wilds.App.Data.TemplateSelectors;
global using global::Wilds.App.Services;
global using global::Wilds.App.UserControls;
global using global::Wilds.App.UserControls.TabBar;
global using global::Wilds.App.UserControls.Widgets;
global using global::Wilds.App.ViewModels;
global using global::Wilds.App.ViewModels.UserControls;
global using global::Wilds.App.ViewModels.UserControls.Widgets;
global using global::Wilds.App.Views;
global using global::Wilds.App.Views.Layouts;
global using global::Wilds.App.Views.Shells;
global using global::Wilds.App.Data.Enums;
global using global::Wilds.App.Data.Messages;
global using global::Wilds.App.Services.DateTimeFormatter;
global using global::Wilds.App.Services.PreviewPopupProviders;
global using global::Wilds.App.Services.Settings;
global using global::Wilds.App.ViewModels.Dialogs;
global using global::Wilds.App.ViewModels.Dialogs.AddItemDialog;
global using global::Wilds.App.ViewModels.Dialogs.FileSystemDialog;
global using global::Wilds.App.Utils.CommandLine;

// Wilds.Core.Storage

global using global::Wilds.Core.Storage;
global using global::Wilds.Core.Storage.Enums;
global using global::Wilds.Core.Storage.EventArguments;
global using global::Wilds.Core.Storage.Extensions;
global using global::OwlCore.Storage;

// Wilds.App.Storage

global using global::Wilds.App.Storage;
global using global::Wilds.App.Storage.Storables;
global using global::Wilds.App.Storage.Watchers;

// Wilds.Shared
global using global::Wilds.Shared;
global using global::Wilds.Shared.Attributes;
global using global::Wilds.Shared.Extensions;
