import {
  createConnection,
  ProposedFeatures,
  InitializeParams,
  TextDocumentSyncKind,
  InitializeResult,
  Diagnostic,
  DiagnosticSeverity,
  Position,
  Range,
  Hover,
  MarkupKind,
  CompletionItem,
  CompletionItemKind,
  InsertTextFormat,
  SignatureHelp,
  SignatureInformation,
  ParameterInformation,
  Location,
  DocumentSymbol,
  SymbolKind,
  WorkspaceEdit,
  SymbolInformation,
  WorkspaceSymbol,
  TextDocuments,
  TextDocument
} from 'vscode-languageserver/node';
import { TextDocument as LSPTextDocument } from 'vscode-languageserver-textdocument';
import * as fs from 'fs';
import * as path from 'path';

const connection = createConnection(ProposedFeatures.all);

const documents: TextDocuments<LSPTextDocument> = new TextDocuments(LSPTextDocument);

let hasConfigurationCapability = false;
let hasWorkspaceFolderCapability = false;

connection.onInitialize((params: InitializeParams) => {
  const capabilities = params.capabilities;

  hasConfigurationCapability = !!(
    capabilities.workspace && !!capabilities.workspace.configuration
  );
  hasWorkspaceFolderCapability = !!(
    capabilities.workspace && !!capabilities.workspace.workspaceFolders
  );

  const result: InitializeResult = {
    capabilities: {
      textDocumentSync: TextDocumentSyncKind.Incremental,
      hoverProvider: true,
      completionProvider: {
        resolveProvider: true,
        triggerCharacters: ['.', '"', '/', '(', ',', ':']
      },
      signatureHelpProvider: {
        triggerCharacters: ['(', ',']
      },
      definitionProvider: true,
      referencesProvider: true,
      documentSymbolProvider: true,
      workspaceSymbolProvider: true,
      renameProvider: {
        prepareProvider: true
      },
      documentFormattingProvider: true
    }
  };

  if (hasWorkspaceFolderCapability) {
    result.capabilities.workspace = {
      workspaceFolders: {
        supported: true
      }
    };
  }

  return result;
});

connection.onInitialized(() => {
  if (hasConfigurationCapability) {
    connection.client.register('workspace/didChangeConfiguration' as any, undefined);
  }
  if (hasWorkspaceFolderCapability) {
    connection.workspace.onDidChangeWorkspaceFolders(_event => {
      connection.console.log('Workspace folder change event received.');
    });
  }
});

const keywords = [
  'let', 'func', 'if', 'elif', 'else', 'while', 'for', 'to',
  'break', 'continue', 'return', 'import', 'true', 'false'
];

const types = [
  'int', 'bool', 'string', 'array', 'map', 'any'
];

const builtinModules = [
  'io', 'array', 'math', 'filesystem'
];

const builtinFunctions = [
  { name: 'print', signature: 'print(value: any)', description: 'Prints a value to the console' },
  { name: 'string', signature: 'string(value: any): string', description: 'Converts a value to string' },
  { name: 'length', signature: 'length(arr: array<any>): int', description: 'Returns the length of an array' }
];

interface SymbolInfo {
  name: string;
  type: string;
  kind: 'variable' | 'function' | 'parameter' | 'struct';
  location: Location;
  references: Location[];
}

class SymbolTable {
  public symbols: Map<string, SymbolInfo[]> = new Map();

  add(symbol: SymbolInfo) {
    const existing = this.symbols.get(symbol.name) || [];
    existing.push(symbol);
    this.symbols.set(symbol.name, existing);
  }

  get(name: string): SymbolInfo[] | undefined {
    return this.symbols.get(name);
  }

  clear() {
    this.symbols.clear();
  }

  getAllSymbols(): SymbolInfo[] {
    return Array.from(this.symbols.values()).flat();
  }
}

const globalSymbolTable = new SymbolTable();
const documentSymbolTables: Map<string, SymbolTable> = new Map();
const documentImports: Map<string, Set<string>> = new Map();

connection.onDidChangeWatchedFiles(change => {
  for (const event of change.changes) {
    if (!documents.get(event.uri)) {
      documentSymbolTables.delete(event.uri);
      documentImports.delete(event.uri);
    }
  }
});

function analyzeDocument(textDocument: LSPTextDocument): SymbolTable {
  const symbolTable = new SymbolTable();
  const text = textDocument.getText();
  const lines = text.split('\n');

  const variablePattern = /\blet\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*(?::\s*([a-zA-Z_][a-zA-Z0-9_<>,\s]*))?/g;
  const functionPattern = /\bfunc\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\(([^)]*)\)\s*(?::\s*([a-zA-Z_][a-zA-Z0-9_<>,\s]*))?/g;

  let match;
  
  while ((match = variablePattern.exec(text)) !== null) {
    const name = match[1];
    const type = match[2] || 'inferred';
    const line = text.substring(0, match.index).split('\n').length - 1;
    
    symbolTable.add({
      name,
      type,
      kind: 'variable',
      location: {
        uri: textDocument.uri,
        range: {
          start: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) },
          end: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) + name.length }
        }
      },
      references: []
    });
  }

  while ((match = functionPattern.exec(text)) !== null) {
    const name = match[1];
    const params = match[2] || '';
    const returnType = match[3] || 'void';
    const line = text.substring(0, match.index).split('\n').length - 1;
    
    symbolTable.add({
      name,
      type: returnType,
      kind: 'function',
      location: {
        uri: textDocument.uri,
        range: {
          start: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) },
          end: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) + name.length }
        }
      },
      references: []
    });
  }

  const structPattern = /\bstruct\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*(?:<[^>]*>)?\s*\{/g;
  while ((match = structPattern.exec(text)) !== null) {
    const name = match[1];
    const line = text.substring(0, match.index).split('\n').length - 1;
    const lineStart = text.lastIndexOf('\n', match.index - 1) + 1;
    symbolTable.add({
      name,
      type: 'struct',
      kind: 'struct',
      location: {
        uri: textDocument.uri,
        range: {
          start: { line, character: match.index - lineStart },
          end: { line, character: match.index - lineStart + name.length }
        }
      },
      references: []
    });
  }

  const importPattern = /^\s*import\s+"([^"]+)"/gm;
  const resolvedImports = new Set<string>();
  while ((match = importPattern.exec(text)) !== null) {
    const resolved = resolveImportPath(textDocument.uri, match[1]);
    if (resolved !== null) resolvedImports.add(resolved);
  }
  documentImports.set(textDocument.uri, resolvedImports);

  return symbolTable;
}

documents.onDidOpen(change => {
  const symbolTable = analyzeDocument(change.document);
  documentSymbolTables.set(change.document.uri, symbolTable);
});

documents.onDidChangeContent(change => {
  const symbolTable = analyzeDocument(change.document);
  documentSymbolTables.set(change.document.uri, symbolTable);
  
  const diagnostics: Diagnostic[] = [];
  const text = change.document.getText();
  
  const badCharPattern = /[^\x20-\x7E\t\n\r]/g;
  let match;
  while ((match = badCharPattern.exec(text)) !== null) {
    const line = text.substring(0, match.index).split('\n').length - 1;
    const lineStart = text.lastIndexOf('\n', match.index - 1) + 1;
    diagnostics.push({
      severity: DiagnosticSeverity.Error,
      range: {
        start: { line, character: match.index - lineStart },
        end: { line, character: match.index - lineStart + 1 }
      },
      message: `Invalid character: ${match[0]}`,
      source: 'prolang'
    });
  }

  connection.sendDiagnostics({ uri: change.document.uri, diagnostics });
});

connection.onHover((params): Hover | null => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return null;

  const wordRange = getWordRangeAtPosition(document, params.position);
  if (!wordRange) return null;

  const word = document.getText(wordRange);
  const symbolTable = documentSymbolTables.get(params.textDocument.uri);

  if (symbolTable) {
    const symbols = symbolTable.get(word);
    if (symbols && symbols.length > 0) {
      const symbol = symbols[0];
      return {
        contents: {
          kind: MarkupKind.Markdown,
          value: `**${symbol.kind}**: \`${word}: ${symbol.type}\``
        }
      };
    }
  }

  if (types.includes(word)) {
    return {
      contents: {
        kind: MarkupKind.Markdown,
        value: `**Type**: \`${word}\`\n\nBuilt-in type in ProLang`
      }
    };
  }

  const builtinFunc = builtinFunctions.find(f => f.name === word);
  if (builtinFunc) {
    return {
      contents: {
        kind: MarkupKind.Markdown,
        value: `**Function**: \`${builtinFunc.signature}\`\n\n${builtinFunc.description}`
      }
    };
  }

  return null;
});

connection.onCompletion((params): CompletionItem[] => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return [];

  const items: CompletionItem[] = [];

  const keywordCompletions: CompletionItem[] = keywords.map(kw => ({
    label: kw,
    kind: CompletionItemKind.Keyword,
    detail: 'Keyword',
    insertText: kw,
    insertTextFormat: InsertTextFormat.PlainText
  }));
  items.push(...keywordCompletions);

  const typeCompletions: CompletionItem[] = types.map(t => ({
    label: t,
    kind: CompletionItemKind.TypeParameter,
    detail: 'Built-in type',
    insertText: t,
    insertTextFormat: InsertTextFormat.PlainText
  }));
  items.push(...typeCompletions);

  const moduleCompletions: CompletionItem[] = builtinModules.map(m => ({
    label: m,
    kind: CompletionItemKind.Module,
    detail: 'Built-in module',
    insertText: m,
    insertTextFormat: InsertTextFormat.PlainText
  }));
  items.push(...moduleCompletions);

  const builtinFunctionCompletions: CompletionItem[] = builtinFunctions.map(f => ({
    label: f.name,
    kind: CompletionItemKind.Function,
    detail: f.signature,
    documentation: f.description,
    insertText: f.name,
    insertTextFormat: InsertTextFormat.PlainText
  }));
  items.push(...builtinFunctionCompletions);

  const symbolTable = documentSymbolTables.get(params.textDocument.uri);
  if (symbolTable) {
    const symbols = symbolTable.getAllSymbols();
    const symbolCompletions: CompletionItem[] = symbols.map(sym => ({
      label: sym.name,
      kind: sym.kind === 'function' ? CompletionItemKind.Function : CompletionItemKind.Variable,
      detail: `${sym.kind}: ${sym.type}`,
      insertText: sym.name,
      insertTextFormat: InsertTextFormat.PlainText
    }));
    items.push(...symbolCompletions);
  }

  const importedUris = getImportedUris(params.textDocument.uri);
  for (const uri of importedUris) {
    if (uri === params.textDocument.uri) continue;
    const importedTable = getOrLoadSymbolTable(uri);
    if (!importedTable) continue;
    for (const sym of importedTable.getAllSymbols()) {
      items.push({
        label: sym.name,
        kind: sym.kind === 'function' ? CompletionItemKind.Function
            : sym.kind === 'struct'   ? CompletionItemKind.Struct
            : CompletionItemKind.Variable,
        detail: `${sym.kind}: ${sym.type} (imported)`,
        insertText: sym.name,
        insertTextFormat: InsertTextFormat.PlainText
      });
    }
  }

  return items;
});

connection.onCompletionResolve((item: CompletionItem): CompletionItem => {
  return item;
});

connection.onSignatureHelp((params): SignatureHelp | null => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return null;

  const text = document.getText();
  const offset = document.offsetAt(params.position);
  
  const functionCallPattern = /([a-zA-Z_][a-zA-Z0-9_]*)\s*\(/g;
  let match;
  let lastMatch = null;
  
  while ((match = functionCallPattern.exec(text)) !== null) {
    const matchEnd = match.index + match[0].length;
    if (matchEnd <= offset) {
      lastMatch = match;
    } else {
      break;
    }
  }

  if (lastMatch) {
    const funcName = lastMatch[1];
    const builtinFunc = builtinFunctions.find(f => f.name === funcName);
    
    if (builtinFunc) {
      const signatureInfo: SignatureInformation = {
        label: builtinFunc.signature,
        documentation: builtinFunc.description
      };

      const paramMatch = builtinFunc.signature.match(/\(([^)]*)\)/);
      if (paramMatch && paramMatch[1]) {
        const params = paramMatch[1].split(',').map(p => p.trim()).filter(p => p);
        signatureInfo.parameters = params.map(p => {
          const [paramName] = p.split(':');
          return {
            label: p,
            documentation: paramName
          } as ParameterInformation;
        });
      }

      return {
        signatures: [signatureInfo],
        activeSignature: 0,
        activeParameter: 0
      };
    }
  }

  return null;
});

connection.onDefinition((params): Location | null => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return null;

  const wordRange = getWordRangeAtPosition(document, params.position);
  if (!wordRange) return null;

  const word = document.getText(wordRange);

  // (a) current document
  const currentTable = documentSymbolTables.get(params.textDocument.uri);
  if (currentTable) {
    const syms = currentTable.get(word);
    if (syms?.length) return syms[0].location;
  }

  // (b) imported files (transitive) — load from disk if not open
  const importedUris = getImportedUris(params.textDocument.uri);
  for (const uri of importedUris) {
    if (uri === params.textDocument.uri) continue;
    const table = getOrLoadSymbolTable(uri);
    const syms = table?.get(word);
    if (syms?.length) return syms[0].location;
  }

  // (c) fallback: all open documents
  for (const [uri, table] of documentSymbolTables.entries()) {
    if (uri === params.textDocument.uri || importedUris.has(uri)) continue;
    const syms = table.get(word);
    if (syms?.length) return syms[0].location;
  }

  return null;
});

connection.onReferences((params): Location[] | null => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return null;

  const wordRange = getWordRangeAtPosition(document, params.position);
  if (!wordRange) return null;

  const word = document.getText(wordRange);
  const locations: Location[] = [];

  const urisToSearch = new Set<string>();
  for (const openUri of documentSymbolTables.keys()) {
    for (const u of getImportedUris(openUri)) urisToSearch.add(u);
  }

  const wordRegex = new RegExp(`\\b${escapeRegex(word)}\\b`, 'g');
  for (const uri of urisToSearch) {
    let text: string | null = null;
    const liveDoc = documents.get(uri);
    if (liveDoc) {
      text = liveDoc.getText();
    } else {
      const fsPath = uriToFsPath(uri);
      if (fs.existsSync(fsPath)) {
        try { text = fs.readFileSync(fsPath, 'utf-8'); } catch { /* skip */ }
      }
    }
    if (!text) continue;
    wordRegex.lastIndex = 0;
    let match;
    while ((match = wordRegex.exec(text)) !== null) {
      const line = text.substring(0, match.index).split('\n').length - 1;
      const lineStart = text.lastIndexOf('\n', match.index - 1) + 1;
      locations.push({
        uri,
        range: {
          start: { line, character: match.index - lineStart },
          end: { line, character: match.index - lineStart + word.length }
        }
      });
    }
  }

  return locations.length > 0 ? locations : null;
});

connection.onDocumentSymbol((params): DocumentSymbol[] => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return [];

  const symbols: DocumentSymbol[] = [];
  const text = document.getText();
  const lines = text.split('\n');

  const variablePattern = /\blet\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*(?::\s*([a-zA-Z_][a-zA-Z0-9_<>,\s]*))?/g;
  const functionPattern = /\bfunc\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\(([^)]*)\)\s*(?::\s*([a-zA-Z_][a-zA-Z0-9_<>,\s]*))?/g;

  let match;
  
  while ((match = variablePattern.exec(text)) !== null) {
    const name = match[1];
    const type = match[2] || 'inferred';
    const line = text.substring(0, match.index).split('\n').length - 1;
    
    symbols.push({
      name,
      kind: SymbolKind.Variable,
      range: {
        start: { line, character: 0 },
        end: { line, character: lines[line].length }
      },
      selectionRange: {
        start: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) },
        end: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) + name.length }
      },
      detail: type
    });
  }

  while ((match = functionPattern.exec(text)) !== null) {
    const name = match[1];
    const params = match[2] || '';
    const returnType = match[3] || 'void';
    const line = text.substring(0, match.index).split('\n').length - 1;
    
    const funcEndLine = findMatchingBrace(text, match.index + match[0].length);
    
    symbols.push({
      name,
      kind: SymbolKind.Function,
      range: {
        start: { line, character: 0 },
        end: { line: funcEndLine, character: lines[funcEndLine]?.length || 0 }
      },
      selectionRange: {
        start: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) },
        end: { line, character: match.index - text.lastIndexOf('\n', match.index - 1) + name.length }
      },
      detail: `(${params}): ${returnType}`
    });
  }

  const structPat = /\bstruct\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*(?:<[^>]*>)?\s*\{/g;
  while ((match = structPat.exec(text)) !== null) {
    const name = match[1];
    const structLine = text.substring(0, match.index).split('\n').length - 1;
    const structEnd  = findMatchingBrace(text, match.index + match[0].length - 1);
    const lineStart  = text.lastIndexOf('\n', match.index - 1) + 1;
    symbols.push({
      name,
      kind: SymbolKind.Struct,
      range: {
        start: { line: structLine, character: 0 },
        end:   { line: structEnd, character: lines[structEnd]?.length || 0 }
      },
      selectionRange: {
        start: { line: structLine, character: match.index - lineStart },
        end:   { line: structLine, character: match.index - lineStart + name.length }
      },
      detail: 'struct'
    });
  }

  return symbols;
});

function findMatchingBrace(text: string, startPos: number): number {
  let depth = 0;
  let foundOpen = false;
  
  for (let i = startPos; i < text.length; i++) {
    if (text[i] === '{') {
      depth++;
      foundOpen = true;
    } else if (text[i] === '}') {
      depth--;
      if (foundOpen && depth === 0) {
        return text.substring(0, i).split('\n').length - 1;
      }
    }
  }
  
  return text.substring(0, startPos).split('\n').length - 1;
}

connection.onWorkspaceSymbol((params): SymbolInformation[] | null => {
  const symbols: SymbolInformation[] = [];
  
  for (const [uri, symbolTable] of documentSymbolTables.entries()) {
    for (const symbolInfo of symbolTable.getAllSymbols()) {
      if (symbolInfo.name.toLowerCase().includes(params.query.toLowerCase())) {
        symbols.push({
          name: symbolInfo.name,
          kind: symbolInfo.kind === 'function' ? SymbolKind.Function : SymbolKind.Variable,
          location: symbolInfo.location
        });
      }
    }
  }
  
  return symbols.length > 0 ? symbols : null;
});

connection.onPrepareRename((params): Range | null => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return null;

  return getWordRangeAtPosition(document, params.position);
});

connection.onRenameRequest(async (params): Promise<WorkspaceEdit | null> => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return null;

  const references = await connection.sendRequest<Location[]>('textDocument/references', {
    textDocument: params.textDocument,
    position: params.position,
    context: { includeDeclaration: true }
  });

  const changes: { [uri: string]: any[] } = {};
  
  references.forEach(loc => {
    if (!changes[loc.uri]) {
      changes[loc.uri] = [];
    }
    changes[loc.uri].push({
      range: loc.range,
      newText: params.newName
    });
  });

  return { changes };
});

connection.onDocumentFormatting((params): any[] => {
  const document = documents.get(params.textDocument.uri);
  if (!document) return [];

  const text = document.getText();
  const lines = text.split('\n');
  
  const formattedLines = lines.map((line, index) => {
    let indentLevel = 0;
    
    for (let i = 0; i < index; i++) {
      const prevLine = lines[i].trim();
      if (prevLine.match(/\b(if|elif|else|while|for|func)\b.*\{/) || prevLine.endsWith('{')) {
        indentLevel++;
      }
      if (prevLine.startsWith('}') || prevLine.match(/^\s*(elif|else)\b/)) {
        indentLevel = Math.max(0, indentLevel - 1);
      }
    }
    
    const indent = ' '.repeat(indentLevel * 4);
    return indent + line.trim();
  });

  const fullRange: Range = {
    start: { line: 0, character: 0 },
    end: { line: lines.length, character: 0 }
  };

  return [{
    range: fullRange,
    newText: formattedLines.join('\n')
  }];
});

function uriToFsPath(uri: string): string {
  let p = decodeURIComponent(uri.replace(/^file:\/\//, ''));
  if (/^\/[A-Za-z]:/.test(p)) p = p.slice(1);
  return p.replace(/\//g, path.sep);
}

function fsPathToUri(fsPath: string): string {
  const normalized = fsPath.replace(/\\/g, '/');
  if (/^[A-Za-z]:/.test(normalized)) return 'file:///' + encodeURI(normalized);
  return 'file://' + encodeURI(normalized);
}

function resolveImportPath(importerUri: string, importPath: string): string | null {
  if (/^[a-zA-Z]+:/.test(importPath)) return null;
  if (!importPath.endsWith('.prl')) return null;
  const importerFsPath = uriToFsPath(importerUri);
  const importerDir = path.dirname(importerFsPath);
  return fsPathToUri(path.resolve(importerDir, importPath));
}

function getOrLoadSymbolTable(fileUri: string): SymbolTable | null {
  const cached = documentSymbolTables.get(fileUri);
  if (cached) return cached;
  const fsPath = uriToFsPath(fileUri);
  if (!fs.existsSync(fsPath)) return null;
  try {
    const content = fs.readFileSync(fsPath, 'utf-8');
    const syntheticDoc = LSPTextDocument.create(fileUri, 'prolang', 0, content);
    const table = analyzeDocument(syntheticDoc);
    documentSymbolTables.set(fileUri, table);
    return table;
  } catch { return null; }
}

function getImportedUris(startUri: string, visited: Set<string> = new Set()): Set<string> {
  if (visited.has(startUri)) return visited;
  visited.add(startUri);
  const imports = documentImports.get(startUri);
  if (imports) for (const uri of imports) getImportedUris(uri, visited);
  return visited;
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function getWordRangeAtPosition(document: LSPTextDocument, position: Position): Range | null {
  const text = document.getText();
  const offset = document.offsetAt(position);
  
  let start = offset;
  let end = offset;
  
  while (start > 0 && /[a-zA-Z0-9_]/.test(text[start - 1])) {
    start--;
  }
  
  while (end < text.length && /[a-zA-Z0-9_]/.test(text[end])) {
    end++;
  }
  
  if (start === end) {
    return null;
  }
  
  return {
    start: document.positionAt(start),
    end: document.positionAt(end)
  };
}

documents.listen(connection);
connection.listen();
