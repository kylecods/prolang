import * as path from 'path';
import { workspace, ExtensionContext, commands, window } from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: ExtensionContext) {
  const serverModule = context.asAbsolutePath(path.join('out', 'server.js'));

  const debugOptions = { execArgv: ['--nolazy', '--inspect=6009'] };

  const serverOptions: ServerOptions = {
    run: { module: serverModule, transport: TransportKind.ipc },
    debug: {
      module: serverModule,
      transport: TransportKind.ipc,
      options: debugOptions
    }
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: 'file', language: 'prolang' }],
    synchronize: {
      configurationSection: 'prolang',
      fileEvents: workspace.createFileSystemWatcher('**/*.prl')
    }
  };

  client = new LanguageClient(
    'prolangLanguageServer',
    'ProLang Language Server',
    serverOptions,
    clientOptions
  );

  client.start();

  context.subscriptions.push(
    commands.registerCommand('prolang.runFile', () => {
      const editor = window.activeTextEditor;
      if (editor && editor.document.languageId === 'prolang') {
        const filePath = editor.document.fileName;
        window.showInformationMessage(`Running ProLang file: ${filePath}`);
        executeProLangFile(filePath);
      } else {
        window.showWarningMessage('No ProLang file active');
      }
    })
  );

  context.subscriptions.push(
    commands.registerCommand('prolang.buildProject', () => {
      const workspaceFolders = workspace.workspaceFolders;
      if (workspaceFolders && workspaceFolders.length > 0) {
        const rootPath = workspaceFolders[0].uri.fsPath;
        window.showInformationMessage(`Building ProLang project: ${rootPath}`);
      } else {
        window.showWarningMessage('No workspace folder open');
      }
    })
  );
}

function executeProLangFile(filePath: string) {
  const terminal = window.createTerminal('ProLang');
  terminal.show();
  
  const prolangExe = 'prolang';
  terminal.sendText(`${prolangExe} "${filePath}"`);
}

export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}
