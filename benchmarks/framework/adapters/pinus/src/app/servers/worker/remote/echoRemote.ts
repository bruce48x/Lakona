interface EchoMessage {
  requestId: number;
  payload: number[];
}

export default function (app: any): EchoRemote {
  return new EchoRemote(app);
}

export class EchoRemote {
  public constructor(private readonly app: any) {
  }

  public async echo(message: EchoMessage): Promise<object> {
    return {
      requestId: message.requestId,
      payload: message.payload,
      terminalNode: this.app.getServerId()
    };
  }
}
