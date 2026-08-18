const { app, BrowserWindow, dialog } = require('electron');
const path = require('path');

let mainWindow;
app.whenReady().then(async () => {
  const backend = require('./server');
  backend.setLogDirectory(path.join(app.getPath('logs'), 'MC Mod Migrator'));
  backend.setNativeFolderPicker(async () => {
    const result = await dialog.showOpenDialog(mainWindow, {
      title: '选择 Minecraft mods 文件夹',
      properties: ['openDirectory', 'createDirectory']
    });
    return result.canceled ? '' : result.filePaths[0];
  });
  await backend.ready;
  mainWindow = new BrowserWindow({
    width: 1180,
    height: 860,
    minWidth: 920,
    minHeight: 680,
    backgroundColor: '#0d0f0e',
    autoHideMenuBar: true,
    webPreferences: { contextIsolation: true, nodeIntegration: false }
  });
  await mainWindow.loadURL('http://127.0.0.1:3728/');
});

app.on('window-all-closed', () => app.quit());
