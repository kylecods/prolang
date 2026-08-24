import * as fs from 'fs';
import * as path from 'path';
import { workspace, ExtensionContext, commands, window } from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

/**
 * How the server is found, in order of preference.
 *
 * The compiler *is* the language server — `prolang lsp` — so there is nothing separate to install
 * and no way for the two to be different versions of the language. The last of these exists for
 * people working on ProLang itself, who have a clone and no installed copy.
 */
function resolveServer(): { command: string; args: string[]; label: string } | undefined {
  const configured = workspace.getConfiguration('prolang').get<string>('serverPath');

  if (configured && configured.trim().length > 0) {
    return { command: configured, args: ['lsp', '--stdio'], label: configured };
  }

  const fromEnvironment = process.env.PROLANG_PATH;

  if (fromEnvironment && fromEnvironment.trim().length > 0) {
    return { command: fromEnvironment, args: ['lsp', '--stdio'], label: fromEnvironment };
  }

  const project = compilerProjectInWorkspace();

  if (project) {
    return {
      command: 'dotnet',
      args: ['run', '--project', project, '--', 'lsp', '--stdio'],
      label: `dotnet run --project ${project}`
    };
  }

  // Assume it is on PATH. If it is not, the client reports the spawn failure and `checkForServer`
  // has already offered an explanation.
  return { command: 'prolang', args: ['lsp', '--stdio'], label: 'prolang (from PATH)' };
}

/** The compiler's own project, when the ProLang repository is what is open. */
function compilerProjectInWorkspace(): string | undefined {
  for (const folder of workspace.workspaceFolders ?? []) {
    const project = path.join(folder.uri.fsPath, 'src', 'ProLang', 'ProLang.csproj');

    if (fs.existsSync(project)) {
      return project;
    }
  }

  return undefined;
}

/**
 * Where library imports should resolve from.
 *
 * By default the compiler uses the `std` directory beside itself, which is right everywhere except
 * in the ProLang repository — there, navigating to a library function should land in the file the
 * user can edit rather than in the copy under `bin/`.
 */
function resolveStdRoot(): string | undefined {
  const configured = workspace.getConfiguration('prolang').get<string>('stdPath');

  if (configured && configured.trim().length > 0) {
    return configured;
  }

  for (const folder of workspace.workspaceFolders ?? []) {
    const std = path.join(folder.uri.fsPath, 'std');

    if (fs.existsSync(path.join(std, 'util.prl'))) {
      return std;
    }
  }

  return undefined;
}

export function activate(context: ExtensionContext) {
  const server = resolveServer();

  if (!server) {
    return;
  }

  const args = [...server.args];
  const stdRoot = resolveStdRoot();

  if (stdRoot) {
    args.push(`--std-root=${stdRoot}`);
  }

  const executable = { command: server.command, args, transport: TransportKind.stdio };

  const serverOptions: ServerOptions = { run: executable, debug: executable };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: 'file', language: 'prolang' }],
    synchronize: {
      fileEvents: workspace.createFileSystemWatcher('**/*.prl')
    },
    outputChannelName: 'ProLang'
  };

  client = new LanguageClient('prolang', 'ProLang Language Server', serverOptions, clientOptions);

  client.start().catch((error: unknown) => {
    window.showErrorMessage(
      `The ProLang language server could not be started (${server.label}). ` +
        `Install the compiler with install.ps1 or 'dotnet tool install -g ProLang.Compiler', ` +
        `or set "prolang.serverPath". ${error}`
    );
  });

  context.subscriptions.push(
    commands.registerCommand('prolang.restartServer', async () => {
      await client?.restart();
      window.showInformationMessage('ProLang language server restarted.');
    }),

    commands.registerCommand('prolang.showServerInfo', () => {
      window.showInformationMessage(
        `ProLang server: ${server.label}. Standard library: ${stdRoot ?? 'beside the compiler'}.`
      );
    }),

    commands.registerCommand('prolang.runFile', () => runActiveFile(server.command)),
    commands.registerCommand('prolang.buildFile', () => buildActiveFile(server.command))
  );
}

/** Compiles the active file to an executable beside it and runs it. */
async function runActiveFile(compiler: string) {
  const file = await activeProLangFile();

  if (!file) {
    return;
  }

  const output = path.join(path.dirname(file), 'bin', path.basename(file, '.prl') + '.dll');
  const terminal = window.createTerminal('ProLang');

  terminal.show();
  terminal.sendText(`${quote(compiler)} ${quote(file)} --target=console -o ${quote(output)} && dotnet ${quote(output)}`);
}

/** Compiles the active file without running it. */
async function buildActiveFile(compiler: string) {
  const file = await activeProLangFile();

  if (!file) {
    return;
  }

  const output = path.join(path.dirname(file), 'bin', path.basename(file, '.prl') + '.dll');
  const terminal = window.createTerminal('ProLang');

  terminal.show();
  terminal.sendText(`${quote(compiler)} ${quote(file)} -o ${quote(output)}`);
}

/**
 * The active ProLang file, saved first.
 *
 * The compiler reads from disk, so running an unsaved buffer would compile the previous version —
 * which is the sort of thing that costs someone an afternoon.
 */
async function activeProLangFile(): Promise<string | undefined> {
  const editor = window.activeTextEditor;

  if (!editor || editor.document.languageId !== 'prolang') {
    window.showWarningMessage('Open a ProLang file first.');
    return undefined;
  }

  if (editor.document.isDirty) {
    await editor.document.save();
  }

  return editor.document.uri.fsPath;
}

function quote(value: string): string {
  return value.includes(' ') ? `"${value}"` : value;
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}
