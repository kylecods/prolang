import {
  DebugSession,
  InitializedEvent,
  TerminatedEvent,
  StoppedEvent,
  OutputEvent,
  Thread,
  StackFrame,
  Scope,
  Source,
  Handles
} from '@vscode/debugadapter';
import { DebugProtocol } from '@vscode/debugprotocol';

interface LaunchRequestArguments extends DebugProtocol.LaunchRequestArguments {
  program: string;
  stopOnEntry?: boolean;
  args?: string[];
}

interface IVariable {
  name: string;
  value: string;
  type: string;
  variablesReference: number;
}

interface IStackFrame {
  name: string;
  path?: string;
  line: number;
}

interface IScopes {
  localVars: IVariable[];
  globalVars: IVariable[];
}

class ProLangDebugSession extends DebugSession {
  private _variableHandles = new Handles<IVariable[] | IScopes>();
  private _runtime: ProLangRuntime = new ProLangRuntime();

  public constructor() {
    super();

    this.setDebuggerLinesStartAt1(true);
    this.setDebuggerColumnsStartAt1(true);
  }

  protected initializeRequest(
    response: DebugProtocol.InitializeResponse,
    args: DebugProtocol.InitializeRequestArguments
  ): void {
    response.body = response.body || {};
    response.body.supportsConfigurationDoneRequest = true;
    response.body.supportsEvaluateForHovers = true;
    response.body.supportsStepBack = false;
    response.body.supportsSetVariable = false;
    response.body.supportsConditionalBreakpoints = true;
    response.body.supportsHitConditionalBreakpoints = true;
    response.body.supportsLogPoints = true;
    response.body.exceptionBreakpointFilters = [
      {
        filter: 'runtime',
        label: 'Runtime Errors',
        default: true
      }
    ];

    this.sendResponse(response);
  }

  protected launchRequest(
    response: DebugProtocol.LaunchResponse,
    args: LaunchRequestArguments
  ): void {
    this._runtime.on('stopOnEntry', () => {
      this.sendEvent(new StoppedEvent('entry', 1));
    });

    this._runtime.on('stopOnStep', () => {
      this.sendEvent(new StoppedEvent('step', 1));
    });

    this._runtime.on('stopOnBreakpoint', (id: number) => {
      this.sendEvent(new StoppedEvent('breakpoint', 1));
    });

    this._runtime.on('output', (text: string) => {
      this.sendEvent(new OutputEvent(text + '\n'));
    });

    this._runtime.on('end', () => {
      this.sendEvent(new TerminatedEvent());
    });

    this._runtime.start(args.program, args.stopOnEntry === true, args.args || []);

    this.sendResponse(response);
  }

  protected configurationDoneRequest(
    response: DebugProtocol.ConfigurationDoneResponse,
    args: DebugProtocol.ConfigurationDoneArguments
  ): void {
    super.configurationDoneRequest(response, args);

    this._runtime.run();
  }

  protected setBreakPointsRequest(
    response: DebugProtocol.SetBreakpointsResponse,
    args: DebugProtocol.SetBreakpointsArguments
  ): void {
    const path = args.source.path!;
    const clientLines = args.lines || [];

    const actualBreakpoints = clientLines.map((line: number) => {
      const bp = this._runtime.setBreakpoint(path, this.convertClientLineToDebugger(line));
      return {
        verified: bp !== null,
        line: bp !== null ? this.convertDebuggerLineToClient(bp) : undefined
      } as DebugProtocol.Breakpoint;
    });

    response.body = { breakpoints: actualBreakpoints };
    this.sendResponse(response);
  }

  protected threadsRequest(response: DebugProtocol.ThreadsResponse): void {
    response.body = {
      threads: [new Thread(1, 'main')]
    };
    this.sendResponse(response);
  }

  protected stackTraceRequest(
    response: DebugProtocol.StackTraceResponse,
    args: DebugProtocol.StackTraceArguments
  ): void {
    const startFrame = typeof args.startFrame === 'number' ? args.startFrame : 0;
    const maxLevels = typeof args.levels === 'number' ? args.levels : 1000;

    const rawStackFrames = this._runtime.stackTrace(startFrame, maxLevels);

    const stackFrames = rawStackFrames.map((frame: IStackFrame, i: number) => {
      return new StackFrame(
        i,
        frame.name,
        frame.path ? this.createSource(frame.path) : undefined,
        this.convertDebuggerLineToClient(frame.line),
        0
      );
    });

    response.body = { stackFrames, totalFrames: rawStackFrames.length };
    this.sendResponse(response);
  }

  protected scopesRequest(
    response: DebugProtocol.ScopesResponse,
    args: DebugProtocol.ScopesArguments
  ): void {
    const scopesData: IScopes = {
      localVars: this._runtime.getLocalVariables(),
      globalVars: this._runtime.getGlobalVariables()
    };
    
    const scopes = [
      new Scope('Local', this._variableHandles.create(scopesData), false),
      new Scope('Global', this._variableHandles.create(scopesData), false)
    ];

    response.body = { scopes };
    this.sendResponse(response);
  }

  protected variablesRequest(
    response: DebugProtocol.VariablesResponse,
    args: DebugProtocol.VariablesArguments
  ): void {
    const variables = this._variableHandles.get(args.variablesReference);
    
    let vars: DebugProtocol.Variable[] = [];
    if (variables && Array.isArray(variables)) {
      vars = variables.map((v: IVariable) => ({
        name: v.name,
        value: v.value,
        type: v.type,
        variablesReference: v.variablesReference
      }));
    } else if (variables && 'localVars' in variables) {
      const scopes = variables as IScopes;
      vars = scopes.localVars.map((v: IVariable) => ({
        name: v.name,
        value: v.value,
        type: v.type,
        variablesReference: v.variablesReference
      }));
    }

    response.body = { variables: vars };
    this.sendResponse(response);
  }

  protected continueRequest(
    response: DebugProtocol.ContinueResponse,
    args: DebugProtocol.ContinueArguments
  ): void {
    this._runtime.continue();
    this.sendResponse(response);
  }

  protected nextRequest(
    response: DebugProtocol.NextResponse,
    args: DebugProtocol.NextArguments
  ): void {
    this._runtime.step();
    this.sendResponse(response);
  }

  protected stepInRequest(
    response: DebugProtocol.StepInResponse,
    args: DebugProtocol.StepInArguments
  ): void {
    this._runtime.stepIn();
    this.sendResponse(response);
  }

  protected stepOutRequest(
    response: DebugProtocol.StepOutResponse,
    args: DebugProtocol.StepOutArguments
  ): void {
    this._runtime.stepOut();
    this.sendResponse(response);
  }

  protected evaluateRequest(
    response: DebugProtocol.EvaluateResponse,
    args: DebugProtocol.EvaluateArguments
  ): void {
    const result = this._runtime.evaluate(args.expression);

    response.body = {
      result: result.value,
      type: result.type,
      variablesReference: 0
    };
    this.sendResponse(response);
  }

  private createSource(filePath: string): Source {
    return new Source(filePath, this.convertClientLineToDebugger(1) === 1 ? undefined : filePath);
  }
}

class ProLangRuntime {
  private _currentLine = 0;
  private _stopped = false;
  private _breakPoints: Map<string, Set<number>> = new Map();
  private _variables: Map<string, any> = new Map();
  private _eventListeners: Map<string, Function[]> = new Map();

  public start(program: string, stopOnEntry: boolean, args: string[]): void {
    this._currentLine = 0;
    this._stopped = false;

    if (stopOnEntry) {
      this.emit('stopOnEntry');
    }
  }

  public run(): void {
    while (!this._stopped) {
      this._currentLine++;
      
      if (this._breakPoints.has('current') && this._breakPoints.get('current')!.has(this._currentLine)) {
        this._stopped = true;
        this.emit('stopOnBreakpoint', this._currentLine);
        return;
      }

      this.emit('output', `Line ${this._currentLine}`);

      if (this._currentLine > 100) {
        this._stopped = true;
        this.emit('end');
      }
    }
  }

  public setBreakpoint(path: string, line: number): number | null {
    if (!this._breakPoints.has(path)) {
      this._breakPoints.set(path, new Set());
    }
    this._breakPoints.get(path)!.add(line);
    return line;
  }

  public continue(): void {
    this._stopped = false;
    this.run();
  }

  public step(): void {
    this._stopped = false;
    this._currentLine++;
    this.emit('stopOnStep');
  }

  public stepIn(): void {
    this.step();
  }

  public stepOut(): void {
    this.step();
  }

  public stackTrace(startFrame: number, maxLevels: number): IStackFrame[] {
    return [
      {
        name: 'main',
        path: 'program.prl',
        line: this._currentLine
      }
    ];
  }

  public getLocalVariables(): IVariable[] {
    return Array.from(this._variables.entries()).map(([name, value]) => ({
      name,
      value: String(value),
      type: typeof value,
      variablesReference: 0
    }));
  }

  public getGlobalVariables(): IVariable[] {
    return this.getLocalVariables();
  }

  public evaluate(expression: string): { value: string; type: string } {
    return {
      value: 'undefined',
      type: 'undefined'
    };
  }

  private emit(event: string, ...args: any[]): void {
    const listeners = this._eventListeners.get(event) || [];
    listeners.forEach(listener => listener(...args));
  }

  public on(event: string, listener: Function): void {
    if (!this._eventListeners.has(event)) {
      this._eventListeners.set(event, []);
    }
    this._eventListeners.get(event)!.push(listener);
  }
}

ProLangDebugSession.run(ProLangDebugSession);
