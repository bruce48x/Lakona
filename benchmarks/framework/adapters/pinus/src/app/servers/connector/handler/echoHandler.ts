interface EchoMessage {
  requestId: number;
  payload: number[];
}

export default function (app: any): EchoHandler {
  return new EchoHandler(app);
}

export class EchoHandler {
  public constructor(private readonly app: any) {
  }

  public async echo(message: EchoMessage, _session: any): Promise<object> {
    return {
      requestId: message.requestId,
      payload: message.payload,
      terminalNode: this.app.getServerId()
    };
  }
}
