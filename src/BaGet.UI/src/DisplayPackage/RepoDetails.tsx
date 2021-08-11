import { Icon } from 'office-ui-fabric-react/lib/Icon'
import { Link } from 'office-ui-fabric-react';
import * as React from 'react';

import './RepoDetails.css'

interface IRepoDetailsProps {
  url: string;
}

interface IRepoDetailsState {
  openIssues: number;
  openPulls: number;
  lastCommit: Date;
  detailsFound: boolean;
}

class RepoDetails extends React.Component<IRepoDetailsProps, IRepoDetailsState> {

  private controller: AbortController;

  constructor(props: IRepoDetailsProps) {
    super(props);

    this.controller = new AbortController();

    this.state = {
      openIssues: 0,
      openPulls: 0,
      lastCommit: new Date(),
      detailsFound: false,
    };
  }

  private getRepoOwnerAndName(repoUrl: string) {
    const frags = repoUrl.split("/");
    let retval = [];

    for(var i = frags.length - 1; i > 0; i--) {
      if(frags[i].length > 0) {
        retval.push(frags[i])
      }

      if(retval.length == 2) {
        break;
      }
    }

    return retval.reverse();
  }

  private buildGitHubUrl(details: Array<string>) {
    return `https://api.github.com/repos/${details[0]}/${details[1]}`;
  }

  public componentDidMount() {
    if (this.props.url) {
      const ownerAndName = this.getRepoOwnerAndName(this.props.url);
      
      if(ownerAndName.length == 2) {
        console.log(ownerAndName);
        fetch(this.buildGitHubUrl(ownerAndName), {signal: this.controller.signal}).then(response => {
            return response.json();
        }).then(json => {
          console.log(json);
          this.setState({
            detailsFound: true,
            openIssues: json['open_issues'],
            openPulls: 0,
            lastCommit: new Date(json['pushed_at'])
          });
        // tslint:disable-next-line:no-console
        }).catch((e) => console.log("Failed to load dependents.", e));
      }

      this.setState({
        openIssues: 100,
        openPulls: 23,
        lastCommit: new Date(2020, 8, 21)
      });
    }
  }

  public render() {
    if (!this.state.detailsFound) {
      return null;
    }

    return (  
      <div>
        <h2>Project Details</h2>
        
        <div className="project-stats">
          <Link href={this.props.url + "/issues"}><Icon iconName="Error" className="ms-Icon issues-icon" />{this.state.openIssues}</Link>
          {/* <Link href={this.props.url + "/pulls"}><Icon iconName="BranchPullRequest" className="ms-Icon pulls-icon" />{this.state.openPulls}</Link> */}
        </div>

        <p className="last-committed">Last pushed on {this.state.lastCommit.toLocaleDateString()}</p>
      </div>
    );
  }
}

export default RepoDetails;

