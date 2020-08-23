import { Icon } from 'office-ui-fabric-react/lib/Icon'
import * as React from 'react';

interface IRepoDetailsProps {
  url: string;
}

interface IRepoDetailsState {
  openIssues: number;
  openPulls: number;
  lastCommit: Date;
}

class RepoDetails extends React.Component<IRepoDetailsProps, IRepoDetailsState> {

  constructor(props: IRepoDetailsProps) {
    super(props);

    this.state = {
      openIssues: 0,
      openPulls: 0,
      lastCommit: new Date()
    };
  }

  public componentDidMount() {
    this.setState({
      openIssues: 100,
      openPulls: 23,
      lastCommit: new Date(2020, 8, 21)
    });
  }

  public render() {
    if (!this.props.url) {
      return null;
    }

    return (  
      <div>
        <h2>Project Details</h2>
        
        <div className="project-stats">
          <span><Icon iconName="Error" className="ms-Icon" />{this.state.openIssues}</span>
          <span><Icon iconName="BranchPullRequest" className="ms-Icon" />{this.state.openPulls}</span>
        </div>
        
        <p className="last-committed">Last committed on {this.state.lastCommit.toLocaleDateString()}</p>
      </div>
    );
  }
}

export default RepoDetails;

